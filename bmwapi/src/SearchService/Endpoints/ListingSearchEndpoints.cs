using System.Globalization;
using Contracts;
using Meilisearch;
using SearchService.Data;
using SearchService.Models;
using SearchService.Services;
using static SearchService.Data.SearchIndex;

namespace SearchService.Endpoints;

/// <summary>Filters chosen in the app (chips, filter sheet, My Garage, pills). Everything optional.</summary>
public class ListingSearchRequest
{
    public string? Q { get; set; }
    public string? PartType { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
    public string? Category { get; set; }
    /// <summary>CommonPart, VehicleSpecificPart or Vehicle.</summary>
    public string? Kind { get; set; }
    public string? Condition { get; set; }
    public string? BrandType { get; set; }
    public int? MinPrice { get; set; }
    public int? MaxPrice { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public double? RadiusKm { get; set; }
    /// <summary>A smaller radius than the default one (filter sheet "Distance").</summary>
    public double? MaxDistanceKm { get; set; }
    /// <summary>Country-wide results when the location is unknown (IN, US, NG, IE).</summary>
    public string? Country { get; set; }
    /// <summary>Only this seller's listings (username) – "more from this seller".</summary>
    public string? Seller { get; set; }
    /// <summary>Auction or FixedPrice.</summary>
    public string? SaleType { get; set; }
    /// <summary>Pickup or Shipping: only listings that offer it.</summary>
    public string? Delivery { get; set; }
    /// <summary>"Seller rating 4★ &amp; up": only sellers with at least this average.</summary>
    public double? MinSellerRating { get; set; }
    /// <summary>best (default), endingSoon, newest, priceAsc, priceDesc, nearest, topRated (top-rated sellers).</summary>
    public string? Sort { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

public record FacetValue(string Value, int Count);

public class ListingSearchResponse
{
    public List<SearchResultItem> Results { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageCount { get; set; }
    /// <summary>What the search understood from the text (for the chips in the search bar).</summary>
    public List<QueryChip> Chips { get; set; } = [];
    public string Keywords { get; set; } = "";
    public Dictionary<string, List<FacetValue>> Facets { get; set; } = [];
    public int? PriceMin { get; set; }
    public int? PriceMax { get; set; }
}

public record Suggestion(string Group, string Label, List<QueryChip> Chips, string? Subtitle = null);

/// <summary>
/// E-commerce style search (design doc §7): understands the text, filters by fitment / part type / size / part number,
/// within the user's radius (or country), ranks (part number → exact vehicle + part type → make + model → universal /
/// size → keywords; ties nearest first), and returns facets, badges and chips. Never relaxes the search silently.
/// </summary>
public static class ListingSearchEndpoints
{
    private const int MaxCandidates = 1000;

    public static async Task<IResult> Search(MeilisearchClient client, SearchDictionaryProvider dictionaries,
        [AsParameters] ListingSearchRequest request)
    {
        var near = request.Lat is not null || request.Lon is not null;
        if (near && (request.Lat is null || request.Lon is null || !GeoMath.IsValidCoordinate(request.Lat.Value, request.Lon.Value)))
            return Results.BadRequest(new { message = "lat and lon must both be valid coordinates" });

        var dictionary = dictionaries.Current;
        var parsed = QueryParser.Parse(request.Q, dictionary);
        ApplyStructuredFilters(parsed, request, dictionary);

        var filters = new List<string> { $"status = \"Live\" AND auctionEnd > \"{DateTime.UtcNow:o}\"" };
        var radius = Math.Clamp(request.RadiusKm ?? SearchEndpoints.DefaultRadiusKm, 1, SearchEndpoints.MaxRadiusKm);
        if (request.MaxDistanceKm is > 0) radius = Math.Min(radius, request.MaxDistanceKm.Value);
        if (near) filters.Add(SearchEndpoints.BoundingBoxFilter(GeoMath.BoundingBox(request.Lat!.Value, request.Lon!.Value, radius)));
        else if (SearchEndpoints.CountryFilter(request.Country) is { } countryFilter) filters.Add(countryFilter);

        if (!string.IsNullOrEmpty(request.Category)) filters.Add($"category = {Quote(request.Category)}");
        if (!string.IsNullOrEmpty(request.Kind)) filters.Add($"kind = {Quote(request.Kind)}");
        if (!string.IsNullOrEmpty(request.Condition)) filters.Add($"condition = {Quote(request.Condition)}");
        if (!string.IsNullOrEmpty(request.BrandType)) filters.Add($"brandType = {Quote(request.BrandType)}");
        if (!string.IsNullOrEmpty(request.Seller)) filters.Add($"seller = {Quote(request.Seller)}");
        if (request.Delivery is "Pickup" or "Shipping") filters.Add($"deliveryOptions = {Quote(request.Delivery)}");
        if (request.MinSellerRating is > 0 and <= 5)
            filters.Add($"sellerRating >= {request.MinSellerRating.Value.ToString(CultureInfo.InvariantCulture)}");
        // Older documents have no saleType: they are auctions.
        if (request.SaleType == "FixedPrice") filters.Add("saleType = \"FixedPrice\"");
        else if (request.SaleType == "Auction") filters.Add("(saleType = \"Auction\" OR NOT saleType EXISTS)");
        if (request.MinPrice is not null) filters.Add($"currentPrice >= {request.MinPrice.Value.ToString(CultureInfo.InvariantCulture)}");
        if (request.MaxPrice is not null) filters.Add($"currentPrice <= {request.MaxPrice.Value.ToString(CultureInfo.InvariantCulture)}");
        if (UnderstoodFilter(parsed) is { } understood) filters.Add(understood);

        var hits = (SearchResult<Item>)await client.Auctions().SearchAsync<Item>(parsed.Keywords, new SearchQuery
        {
            Filter = string.Join(" AND ", filters),
            Limit = MaxCandidates,
        });

        // Exact distance, position / side, ranking tier and badge.
        var results = new List<SearchResultItem>();
        var order = 0;
        var meiliOrder = new Dictionary<string, int>();
        foreach (var item in hits.Hits)
        {
            meiliOrder[item.Id] = order++;
            var hit = Mapster.TypeAdapter.Adapt<SearchResultItem>(item);
            if (near)
            {
                if (item.Latitude is null || item.Longitude is null) continue;
                hit.DistanceKm = Math.Round(GeoMath.HaversineKm(request.Lat!.Value, request.Lon!.Value, item.Latitude.Value, item.Longitude.Value), 1);
                if (hit.DistanceKm > radius) continue;
            }
            if (parsed.Position is not null && item.Position is not null && item.Position != parsed.Position) continue;
            if (parsed.Side is not null && item.Side is not null && item.Side != "Centre" && item.Side != parsed.Side) continue;

            hit.MatchTier = Tier(item, parsed);
            hit.Badge = Badge(item, parsed);
            results.Add(hit);
        }

        results = Sort(results, request.Sort, near, meiliOrder);

        var size = Math.Clamp(request.PageSize ?? 20, 1, 50);
        var page = Math.Max(request.Page ?? 1, 1);
        return Results.Ok(new ListingSearchResponse
        {
            Results = results.Skip((page - 1) * size).Take(size).ToList(),
            TotalCount = results.Count,
            PageCount = (int)Math.Ceiling(results.Count / (double)size),
            Chips = parsed.Chips(),
            Keywords = parsed.Keywords,
            Facets = Facets(results),
            PriceMin = results.Count > 0 ? results.Min(r => r.CurrentPrice) : null,
            PriceMax = results.Count > 0 ? results.Max(r => r.CurrentPrice) : null,
        });
    }

    /// <summary>Chips / filter-sheet values override what was read from the text.</summary>
    public static void ApplyStructuredFilters(ParsedQuery parsed, ListingSearchRequest request, SearchDictionary dictionary)
    {
        if (!string.IsNullOrWhiteSpace(request.PartType))
            parsed.PartType = dictionary.Data.PartTypes.FirstOrDefault(p => p.Name.Equals(request.PartType, StringComparison.OrdinalIgnoreCase))
                              ?? new DictionaryPartType { Name = request.PartType.Trim() };
        if (!string.IsNullOrWhiteSpace(request.Make)) parsed.Make = request.Make.Trim();
        if (!string.IsNullOrWhiteSpace(request.Model)) parsed.Model = request.Model.Trim();
        if (request.Year is not null) parsed.YearFrom = parsed.YearTo = request.Year;
    }

    /// <summary>
    /// (listings whose fitment covers the vehicle) OR (common parts of that part type – universal, or size-based
    /// matching the size in the query); a part number is an exact match on the normalised numbers.
    /// </summary>
    public static string? UnderstoodFilter(ParsedQuery q)
    {
        if (q.PartNumber is not null)
            return $"partNumbersNormalized = {Quote(q.PartNumber)}";

        var parts = new List<string>();
        var partType = q.PartType is null ? null : $"partType = {Quote(q.PartType.Name)}";
        var vehicle = q.VehicleKey() is { } key ? $"fitKeys = {Quote(key)}" : null;
        var size = q.TyreSize is { } tyre ? $"specKeys = {Quote("tyresize:" + tyre)}" : null;

        if (partType is not null && vehicle is not null)
            return $"(({vehicle} AND {partType}) OR (fitmentMode IN [\"Universal\", \"SizeBased\"] AND {partType}))";
        if (partType is not null)
            return size is null ? partType : $"({partType} AND {size})";
        if (size is not null) parts.Add(size);
        if (vehicle is not null) parts.Add(vehicle);
        return parts.Count == 0 ? null : string.Join(" AND ", parts);
    }

    /// <summary>0 part number · 1 exact vehicle + part type · 2 make + model · 3 universal / size-based · 4 keyword only.</summary>
    public static int Tier(Item item, ParsedQuery q)
    {
        if (q.PartNumber is not null && item.PartNumbersNormalized.Contains(q.PartNumber)) return 0;
        var vehicleKey = q.VehicleKey();
        var makeModelKey = q.Make is null ? null : q.Model is null ? Item.Key(q.Make) : $"{Item.Key(q.Make)}|{Item.Key(q.Model)}";
        var typeMatches = q.PartType is null || string.Equals(item.PartType, q.PartType.Name, StringComparison.OrdinalIgnoreCase);
        if (vehicleKey is not null && q.HasYear && typeMatches && item.FitKeys.Contains(vehicleKey)) return 1;
        if (makeModelKey is not null && typeMatches && item.FitKeys.Contains(makeModelKey)) return q.HasYear ? 2 : 1;
        if (item.FitmentMode is "Universal" or "SizeBased") return q.PartType is not null || q.TyreSize is not null ? 3 : 4;
        return q.PartType is not null && typeMatches ? 2 : 4;
    }

    /// <summary>"Fits your 2016 Toyota Hilux", "Universal", "Fits by size" or "Check fitment" (vehicle without year).</summary>
    public static string? Badge(Item item, ParsedQuery q)
    {
        if (item.Kind == "Vehicle") return null;
        if (item.FitmentMode == "Universal") return "Universal";
        if (item.FitmentMode == "SizeBased") return "Fits by size";
        if (q.HasVehicle && q.Model is not null)
        {
            if (q.HasYear && q.VehicleKey() is { } key && item.FitKeys.Contains(key)) return $"Fits your {q.VehicleLabel()}";
            if (!q.HasYear) return "Check fitment";
        }
        return null;
    }

    private static List<SearchResultItem> Sort(List<SearchResultItem> results, string? sort, bool near, Dictionary<string, int> meiliOrder) =>
        sort switch
        {
            "endingSoon" => results.OrderBy(r => r.AuctionEnd).ToList(),
            "newest" => results.OrderByDescending(r => r.CreatedAt).ToList(),
            "priceAsc" => results.OrderBy(r => r.CurrentPrice).ToList(),
            "priceDesc" => results.OrderByDescending(r => r.CurrentPrice).ToList(),
            // Top-rated sellers: rating, then more ratings first; sellers without ratings last; then the best match.
            "topRated" => results.OrderByDescending(r => r.SellerRating ?? 0).ThenByDescending(r => r.SellerRatingCount)
                .ThenBy(r => r.MatchTier).ToList(),
            "nearest" when near => results.OrderBy(r => r.DistanceKm ?? double.MaxValue).ToList(),
            // Best match: tier, then nearest first, then Meilisearch's text relevance.
            _ => results.OrderBy(r => r.MatchTier)
                .ThenBy(r => near ? r.DistanceKm ?? double.MaxValue : 0)
                .ThenBy(r => meiliOrder.GetValueOrDefault(r.Id))
                .ToList()
        };

    /// <summary>Filter sheet facets from the results: category, part type, make, model, year, condition, brand type, kind.</summary>
    public static Dictionary<string, List<FacetValue>> Facets(List<SearchResultItem> results)
    {
        static List<FacetValue> Count(IEnumerable<string?> values) =>
            values.Where(v => !string.IsNullOrWhiteSpace(v)).GroupBy(v => v!)
                .Select(g => new FacetValue(g.Key, g.Count()))
                .OrderByDescending(f => f.Count).ThenBy(f => f.Value).Take(30).ToList();

        IEnumerable<string> Makes(Item r) => r.Fitments.Count > 0 ? r.Fitments.Select(f => f.Make).Distinct() : r.Make.Length > 0 ? [r.Make] : [];
        IEnumerable<string> Models(Item r) => r.Fitments.Count > 0
            ? r.Fitments.Select(f => $"{f.Make}|{f.Model}").Distinct()
            : r.Model.Length > 0 ? [$"{r.Make}|{r.Model}"] : [];
        IEnumerable<string> Years(Item r) => r.FitKeys.Select(k => k.Split('|')).Where(p => p.Length == 3).Select(p => p[2]).Distinct();

        return new Dictionary<string, List<FacetValue>>
        {
            ["category"] = Count(results.Select(r => r.Category)),
            ["partType"] = Count(results.Select(r => r.PartType)),
            ["make"] = Count(results.SelectMany(Makes)),
            ["model"] = Count(results.SelectMany(Models)),
            ["year"] = Count(results.SelectMany(Years)).OrderByDescending(f => f.Value).ToList(),
            ["condition"] = Count(results.Select(r => r.Condition)),
            ["brandType"] = Count(results.Select(r => r.BrandType)),
            ["kind"] = Count(results.Select(r => r.Kind)),
            ["delivery"] = Count(results.SelectMany(r => r.DeliveryOptions)),
        };
    }

    // ── suggestions ───────────────────────────────────────────────────

    /// <summary>
    /// Suggestions while typing, grouped like an online shop: parts ("Front bumper", "Front bumper – Toyota Hilux 2016"),
    /// vehicles ("Toyota Hilux 2016–2020"), part numbers ("52119-0K935"). Recent searches are added by the app.
    /// </summary>
    public static async Task<IResult> Suggest(MeilisearchClient client, SearchDictionaryProvider dictionaries,
        string? q, string? country, int limit = 5)
    {
        var text = q?.Trim() ?? "";
        if (text.Length < 2) return Results.Ok(new { suggestions = Array.Empty<Suggestion>() });

        var dictionary = dictionaries.Current;
        var parsed = QueryParser.Parse(text, dictionary);
        var normalized = SearchDictionary.Normalize(text);
        var lastWord = normalized.Split(' ').LastOrDefault() ?? "";
        var take = Math.Clamp(limit, 1, 10);
        var suggestions = new List<Suggestion>();
        var vehicleChips = VehicleChips(parsed);

        // Parts: the understood part type (with position / vehicle), then part types starting with the last word.
        if (parsed.PartType is not null)
        {
            var chips = new List<QueryChip> { new("partType", parsed.PartType.Name, parsed.PartLabel()!) };
            suggestions.Add(new Suggestion("parts", Capitalise(parsed.PartLabel()!), chips, parsed.PartType.Category));
            if (parsed.VehicleLabel() is { } vehicle)
                suggestions.Add(new Suggestion("parts", $"{Capitalise(parsed.PartLabel()!)} – {vehicle}", [.. chips, .. vehicleChips], "Fits your vehicle"));
        }
        if (lastWord.Length >= 2)
        {
            foreach (var type in dictionary.Data.PartTypes
                         .Select(p => (Type: p, Match: Matches(p.Name, lastWord) ? p.Name : p.Synonyms.FirstOrDefault(s => Matches(s, lastWord))))
                         .Where(x => x.Match is not null && x.Type.Name != parsed.PartType?.Name)
                         .OrderBy(x => x.Type.Name.Length)
                         .Take(take))
            {
                var label = type.Type.Name;
                var chips = new List<QueryChip> { new("partType", type.Type.Name, type.Type.Name) };
                var subtitle = type.Match != type.Type.Name ? $"“{type.Match}” · {type.Type.Category}" : type.Type.Category;
                if (parsed.VehicleLabel() is { } vehicle)
                    suggestions.Add(new Suggestion("parts", $"{label} – {vehicle}", [.. chips, .. vehicleChips], subtitle));
                else
                    suggestions.Add(new Suggestion("parts", label, chips, subtitle));
            }
        }

        // Vehicles: the understood vehicle (with the years listings cover), then models / makes starting with the last word.
        if (parsed.Make is not null && parsed.Model is not null)
            suggestions.Add(new Suggestion("vehicles", await VehicleWithYearsAsync(client, parsed), vehicleChips));
        if (lastWord.Length >= 2)
        {
            var c = country?.ToUpperInvariant();
            foreach (var (make, model) in dictionary.Data.Makes
                         .SelectMany(m => m.Models.Select(x => (Make: m, Model: x)))
                         .Where(x => Matches(x.Model.Name, lastWord) || x.Model.Aliases.Any(a => Matches(a, lastWord)) ||
                                     (parsed.Model is null && Matches(x.Make.Name, lastWord)))
                         .Where(x => x.Model.Name != parsed.Model)
                         .OrderBy(x => c != null && x.Model.Countries.Contains(c) ? 0 : 1)
                         .ThenBy(x => x.Make.Name).ThenBy(x => x.Model.Name)
                         .Take(take))
            {
                var label = string.Join(" ", new object?[] { parsed.YearFrom, make.Name, model.Name }.Where(x => x is not null));
                var chips = new List<QueryChip> { new("make", make.Name, make.Name), new("model", model.Name, model.Name) };
                if (parsed.YearFrom is not null) chips.Add(new QueryChip("year", $"{parsed.YearFrom}", $"{parsed.YearFrom}"));
                if (parsed.PartType is not null) chips.Insert(0, new QueryChip("partType", parsed.PartType.Name, parsed.PartLabel()!));
                suggestions.Add(new Suggestion("vehicles", parsed.PartType is null ? label : $"{Capitalise(parsed.PartLabel()!)} – {label}", chips));
            }
        }

        // Part numbers from live listings.
        var numberQuery = Contracts.PartNumbers.Normalize(text);
        if (numberQuery is { Length: >= 3 } && numberQuery.Any(char.IsDigit))
        {
            var hits = await client.Auctions().SearchAsync<Item>(text, new SearchQuery
            {
                Filter = $"status = \"Live\"",
                AttributesToSearchOn = ["partNumbers", "partNumbersNormalized"],
                Limit = 20,
            });
            foreach (var number in hits.Hits.SelectMany(h => h.PartNumbers)
                         .Where(n => Contracts.PartNumbers.Normalize(n)?.Contains(numberQuery) == true)
                         .Distinct().Take(take))
                suggestions.Add(new Suggestion("partNumbers", number,
                    [new QueryChip("partNumber", Contracts.PartNumbers.Normalize(number)!, number)]));
        }

        return Results.Ok(new
        {
            suggestions = suggestions
                .GroupBy(s => s.Label, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .GroupBy(s => s.Group).SelectMany(g => g.Take(take))
                .ToList()
        });
    }

    private static List<QueryChip> VehicleChips(ParsedQuery parsed) =>
        parsed.Chips().Where(c => c.Type is "make" or "model" or "year").ToList();

    /// <summary>"Toyota Hilux 2016–2020": the years live listings cover (or the year typed).</summary>
    private static async Task<string> VehicleWithYearsAsync(MeilisearchClient client, ParsedQuery parsed)
    {
        if (parsed.YearFrom is not null) return parsed.VehicleLabel()!;
        var key = $"{Item.Key(parsed.Make)}|{Item.Key(parsed.Model)}";
        var hits = await client.Auctions().SearchAsync<FitKeysOnly>("", new SearchQuery
        {
            Filter = $"fitKeys = {Quote(key)} AND status = \"Live\"",
            Limit = 50,
            AttributesToRetrieve = ["fitKeys"],
        });
        var years = hits.Hits.SelectMany(h => h.FitKeys)
            .Where(k => k.StartsWith(key + "|")).Select(k => int.Parse(k[(key.Length + 1)..], CultureInfo.InvariantCulture)).ToList();
        return years.Count == 0 ? $"{parsed.Make} {parsed.Model}"
            : years.Min() == years.Max() ? $"{parsed.Make} {parsed.Model} {years.Min()}"
            : $"{parsed.Make} {parsed.Model} {years.Min()}–{years.Max()}";
    }

    private sealed class FitKeysOnly
    {
        public List<string> FitKeys { get; set; } = [];
    }

    private static bool Matches(string candidate, string word)
    {
        var c = SearchDictionary.Normalize(candidate);
        return c.StartsWith(word) || c.Split(' ').Any(w => w.StartsWith(word));
    }

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
