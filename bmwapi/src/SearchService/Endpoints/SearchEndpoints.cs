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
        int pageNumber = 1,
        int pageSize = 10
        )
    {
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
}