using System.Globalization;
using Contracts;
using Mapster;
using Meilisearch;
using SearchService.Data;
using SearchService.Models;
using static SearchService.Data.SearchIndex;

namespace SearchService.Endpoints;

public static class SearchEndpoints
{
    public static async Task<IResult> GetAuctionById(MeilisearchClient client, string id)
    {
        try
        {
            var result = await client.Auctions().GetDocumentAsync<Item>(id);

            return Results.Ok(result);
        }
        catch (MeilisearchApiError e) when (e.Code == "document_not_found")
        {
            return Results.NotFound();
        }
    }

    public static async Task<IResult> GetSearchResults(
        MeilisearchClient client,
        string? searchTerm,
        string? seller,
        string? winner,
        string? orderBy,
        string? filterBy,
        string? itemType,
        string? partCategory,
        string? condition,
        string? destination,
        double? lat,
        double? lon,
        double? radiusKm,
        int pageNumber = 1,
        int pageSize = 10
        )
    {
        // "Parts within X km": only when both coordinates are given; otherwise search works as before.
        var near = lat is not null || lon is not null;
        if (near && (lat is null || lon is null || !GeoMath.IsValidCoordinate(lat.Value, lon.Value)))
            return Results.BadRequest(new { message = "lat and lon must both be valid coordinates" });

        var filters = new List<string>();
        if (!string.IsNullOrEmpty(seller))
            filters.Add($"seller = {Quote(seller)}");
        if (!string.IsNullOrEmpty(winner))
            filters.Add($"winner = {Quote(winner)}");
        if (!string.IsNullOrEmpty(filterBy))
        {
            var now = DateTime.UtcNow;
            var dateFilter = filterBy switch
            {
                "live" => $"status = \"Live\" AND auctionEnd > \"{now:o}\"",
                // Ended in any way: Sold, waiting for the seller, Unsold, or past the end but not processed yet.
                "finished" => $"(status IN [\"Sold\", \"ReserveNotMet\", \"Unsold\"] OR auctionEnd < \"{now:o}\")",
                "endingSoon" => $"status = \"Live\" AND auctionEnd > \"{now:o}\" AND auctionEnd < \"{now.AddHours(6):o}\"",
                _ => null
            };
            if (dateFilter != null)
                filters.Add(dateFilter);
        }

        var type = itemType?.ToLowerInvariant() switch
        {
            "vehicle" or "vehicles" => "Vehicle",
            "part" or "parts" => "Part",
            _ => null // "all" or empty
        };
        if (type != null)
            filters.Add($"itemType = {Quote(type)}");
        if (!string.IsNullOrEmpty(partCategory))
            filters.Add($"partCategory = {Quote(partCategory)}");
        if (!string.IsNullOrEmpty(condition))
            filters.Add($"condition = {Quote(condition)}");
        if (!string.IsNullOrEmpty(destination))
            filters.Add($"(container.destinationCountry = {Quote(destination)} OR container.destinationPort = {Quote(destination)})");

        List<string>? sort = orderBy switch
        {
            "make" => ["make:asc", "model:asc"],
            "new" => ["createdAt:desc"],
            "endingSoon" => ["auctionEnd:asc"],
            _ => null
        };

        if (near)
            return await SearchNearAsync(client, searchTerm, filters, sort, orderBy == "distance",
                lat!.Value, lon!.Value, radiusKm, pageNumber, pageSize);

        var query = new SearchQuery
        {
            Page = pageNumber < 1 ? 1 : pageNumber,
            HitsPerPage = pageSize > 50 ? 50 : pageSize,
            Filter = filters.Count > 0 ? string.Join(" AND ", filters) : null,
            Sort = sort,
        };

        var result = (PaginatedSearchResult<Item>)await client.Auctions()
            .SearchAsync<Item>(searchTerm ?? string.Empty, query);

        return Results.Ok(new
        {
            results = result.Hits,
            pageCount = result.TotalPages,
            totalCount = result.TotalHits
        });
    }

    public const double DefaultRadiusKm = 50;
    public const double MaxRadiusKm = 500;

    /// <summary>Most candidates looked at for one "near" search (Meilisearch's default maxTotalHits).</summary>
    private const int MaxNearCandidates = 1000;

    /// <summary>
    /// Items within <paramref name="radiusKm"/> of (lat, lon): a bounding-box pre-filter in Meilisearch
    /// (cheap, may include a few items in the box's corners), then the exact Haversine distance here.
    /// Each result gets distanceKm; orderBy=distance sorts nearest first. Paging happens after the distance check
    /// so page counts are exact.
    /// </summary>
    private static async Task<IResult> SearchNearAsync(MeilisearchClient client, string? searchTerm,
        List<string> filters, List<string>? sort, bool nearestFirst, double lat, double lon, double? radiusKm,
        int pageNumber, int pageSize)
    {
        var radius = Math.Clamp(radiusKm ?? DefaultRadiusKm, 1, MaxRadiusKm);
        filters.Add(BoundingBoxFilter(GeoMath.BoundingBox(lat, lon, radius)));

        var candidates = (SearchResult<Item>)await client.Auctions().SearchAsync<Item>(searchTerm ?? string.Empty,
            new SearchQuery
            {
                Filter = string.Join(" AND ", filters),
                Sort = sort,
                Limit = MaxNearCandidates,
            });

        var within = candidates.Hits
            .Select(item =>
            {
                var hit = item.Adapt<SearchResultItem>();
                hit.DistanceKm = Math.Round(GeoMath.HaversineKm(lat, lon, item.Latitude!.Value, item.Longitude!.Value), 1);
                return hit;
            })
            .Where(hit => hit.DistanceKm <= radius)
            .ToList();

        if (nearestFirst) within = within.OrderBy(hit => hit.DistanceKm).ToList();

        var size = Math.Clamp(pageSize, 1, 50);
        var page = Math.Max(pageNumber, 1);
        return Results.Ok(new
        {
            results = within.Skip((page - 1) * size).Take(size),
            pageCount = (int)Math.Ceiling(within.Count / (double)size),
            totalCount = within.Count
        });
    }

    /// <summary>Latitude/longitude ranges for Meilisearch. Items without a location never match.</summary>
    public static string BoundingBoxFilter(GeoBox box)
    {
        static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        var filter = $"latitude >= {N(box.MinLatitude)} AND latitude <= {N(box.MaxLatitude)}";
        return box.MinLongitude <= -180 && box.MaxLongitude >= 180
            ? $"({filter} AND longitude EXISTS)"
            : $"({filter} AND longitude >= {N(box.MinLongitude)} AND longitude <= {N(box.MaxLongitude)})";
    }
}