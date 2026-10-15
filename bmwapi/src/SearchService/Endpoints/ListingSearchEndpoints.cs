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
    /// <summary>Part or Vehicle (Home's Parts / Vehicles switch); empty = both.</summary>
    public string? ItemType { get; set; }
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

        // Every active filter is part of the Meilisearch query, so the total and the facet counts are exactly what the
        // results are (text, Parts / Vehicles, Auction / Direct sale, category, part type, vehicle, radius, sheet values).
        var hits = (PaginatedSearchResult<Item>)await client.Auctions().SearchAsync<Item>(parsed.Keywords, new SearchQuery
        {
            Filter = string.Join(" AND ", Filters(request, parsed)),
            HitsPerPage = MaxCandidates,
            Page = 1,
            Facets = FacetFields.Values.ToList(),
        });

        // Exact distance, ranking tier and badge.
        var results = new List<SearchResultItem>();
        var order = 0;
        var meiliOrder = new Dictionary<string, int>();
        foreach (var item in hits.Hits)
        {
            meiliOrder[item.Id] = order++;
            var hit = Mapster.TypeAdapter.Adapt<SearchResultItem>(item);
            if (near && item.Latitude is not null && item.Longitude is not null)
                hit.DistanceKm = Math.Round(GeoMath.HaversineKm(request.Lat!.Value, request.Lon!.Value, item.Latitude.Value, item.Longitude.Value), 1);
            hit.MatchTier = Tier(item, parsed);
            hit.Badge = Badge(item, parsed);
            results.Add(hit);
        }

        results = Sort(results, request.Sort, near, meiliOrder);

        var facets = ToFacets(hits.FacetDistribution);
        // A chosen category / part type / vehicle keeps its siblings with their counts (e.g. the other categories),
        // counted with every other filter still applied.
        foreach (var (names, exclude) in DisjunctiveFacets(request, parsed))
        {
            var other = await client.Auctions().SearchAsync<Item>(parsed.Keywords, new SearchQuery
            {
                Filter = string.Join(" AND ", Filters(request, parsed, exclude)),
                Limit = 0,
                Facets = names.Select(n => FacetFields[n]).ToList(),
            });
            foreach (var (name, values) in ToFacets(other.FacetDistribution).Where(f => names.Contains(f.Key)))
                facets[name] = values;
        }

        var size = Math.Clamp(request.PageSize ?? 20, 1, 50);
        var page = Math.Max(request.Page ?? 1, 1);
        return Results.Ok(new ListingSearchResponse
        {
            Results = results.Skip((page - 1) * size).Take(size).ToList(),
            TotalCount = hits.TotalHits,
            PageCount = (int)Math.Ceiling(hits.TotalHits / (double)size),
            Chips = parsed.Chips(),
            Keywords = parsed.Keywords,
            Facets = facets,
            PriceMin = results.Count > 0 ? results.Min(r => r.CurrentPrice) : null,
            PriceMax = results.Count > 0 ? results.Max(r => r.CurrentPrice) : null,
        });
    }

    /// <summary>Response facet name → Meilisearch attribute.</summary>
    public static readonly IReadOnlyDictionary<string, string> FacetFields = new Dictionary<string, string>
    {
        ["category"] = "category",
        ["partType"] = "partType",
        ["make"] = "makes",
        ["model"] = "models",
        ["year"] = "years",
        ["condition"] = "condition",
        ["brandType"] = "brandType",
        ["kind"] = "kind",
        ["delivery"] = "deliveryOptions",
        ["saleType"] = "saleType",
        ["itemType"] = "listingType",
    };

    [Flags]
    public enum Exclude
    {
        None = 0,
        Category = 1,
        PartType = 2,
        Vehicle = 4,
    }

    private static double Radius(ListingSearchRequest request)
    {
        var radius = Math.Clamp(request.RadiusKm ?? SearchEndpoints.DefaultRadiusKm, 1, SearchEndpoints.MaxRadiusKm);
        return request.MaxDistanceKm is > 0 ? Math.Min(radius, request.MaxDistanceKm.Value) : radius;
    }

    /// <summary>"Part" / "Vehicle" (also accepts "Parts" / "Vehicles"); null = both.</summary>
    public static string? ListingType(string? itemType) => itemType?.Trim().ToLowerInvariant() switch
    {
        "part" or "parts" => "Part",
        "vehicle" or "vehicles" => "Vehicle",
        _ => null,
    };

    /// <summary>The Meilisearch filter for a search. <paramref name="exclude"/> leaves filters out (for sibling facet counts).</summary>
    public static List<string> Filters(ListingSearchRequest request, ParsedQuery parsed, Exclude exclude = Exclude.None)
    {
        var filters = new List<string> { $"status = \"Live\" AND auctionEnd > \"{DateTime.UtcNow:o}\"" };
        if (request.Lat is not null && request.Lon is not null)
        {
            var meters = Math.Round(Radius(request) * 1000);
            filters.Add(string.Create(CultureInfo.InvariantCulture, $"_geoRadius({request.Lat.Value}, {request.Lon.Value}, {meters})"));
        }
        else if (SearchEndpoints.CountryFilter(request.Country) is { } countryFilter) filters.Add(countryFilter);

        if (ListingType(request.ItemType) is { } listingType) filters.Add($"listingType = {Quote(listingType)}");
        if (!string.IsNullOrEmpty(request.Category) && !exclude.HasFlag(Exclude.Category)) filters.Add($"category = {Quote(request.Category)}");
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

        // Front / rear and left / right from the text: items without a position / side still match.
        if (parsed.Position is not null)
            filters.Add($"(NOT position EXISTS OR position IS NULL OR position = {Quote(parsed.Position)})");
        if (parsed.Side is not null)
            filters.Add($"(NOT side EXISTS OR side IS NULL OR side = \"Centre\" OR side = {Quote(parsed.Side)})");

        var understood = parsed;
        if (exclude.HasFlag(Exclude.PartType) || exclude.HasFlag(Exclude.Vehicle))
        {
            understood = parsed.Copy();
            if (exclude.HasFlag(Exclude.PartType)) understood.PartType = null;
            if (exclude.HasFlag(Exclude.Vehicle))
            {
                understood.Make = understood.Model = null;
                understood.YearFrom = understood.YearTo = null;
            }
        }
        if (UnderstoodFilter(understood) is { } filter) filters.Add(filter);
        return filters;
    }

    /// <summary>
    /// Which facets need their own count query because the user picked a value for them (category pill, part type chip,
    /// make / model chip). What the typed text means always counts: "bumper" keeps only categories that have bumpers.
    /// </summary>
    private static IEnumerable<(string[] Names, Exclude Exclude)> DisjunctiveFacets(ListingSearchRequest request, ParsedQuery parsed)
    {
        var pickedPartType = !string.IsNullOrWhiteSpace(request.PartType);
        if (!string.IsNullOrEmpty(request.Category) || pickedPartType)
            yield return (["category"], Exclude.Category | (pickedPartType ? Exclude.PartType : Exclude.None));
        if (pickedPartType)
            yield return (["partType"], Exclude.PartType);
        if (!string.IsNullOrWhiteSpace(request.Make) || !string.IsNullOrWhiteSpace(request.Model))
            yield return (["make", "model"], Exclude.Vehicle);
    }

    private static Dictionary<string, List<FacetValue>> ToFacets(IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>? distribution)
    {
        var result = new Dictionary<string, List<FacetValue>>();
        foreach (var (name, field) in FacetFields)
        {
            var values = distribution?.GetValueOrDefault(field);
            var list = (values ?? new Dictionary<string, int>())
                .Where(v => v.Value > 0 && !string.IsNullOrWhiteSpace(v.Key))
                .Select(v => new FacetValue(v.Key, v.Value))
                .OrderByDescending(f => f.Count).ThenBy(f => f.Value).ToList();
            result[name] = name == "year" ? list.OrderByDescending(f => f.Value).ToList() : list;
        }
        return result;
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

    // ── suggestions ───────────────────────────────────────────────────

    /// <summary>
    /// Suggestions while typing, grouped like an online shop: categories ("Brakes"), parts ("Front bumper", "Front bumper – Toyota Hilux 2016"),
    /// vehicles ("Toyota Hilux 2016–2020"), part numbers ("52119-0K935"). Recent searches are added by the app.
    /// itemType limits them to the Parts / Vehicles choice: Vehicles → only vehicles; Parts → parts, part numbers and
    /// "Parts for Toyota Hilux 2016–2020" (group "fits").
    /// </summary>
    public static async Task<IResult> Suggest(MeilisearchClient client, SearchDictionaryProvider dictionaries,
        string? q, string? country, string? itemType = null, int limit = 5)
    {
        var listingType = ListingType(itemType);
        var text = q?.Trim() ?? "";
        if (text.Length < 2) return Results.Ok(new { suggestions = Array.Empty<Suggestion>() });

        var dictionary = dictionaries.Current;
        var parsed = QueryParser.Parse(text, dictionary);
        var normalized = SearchDictionary.Normalize(text);
        var lastWord = normalized.Split(' ').LastOrDefault() ?? "";
        var take = Math.Clamp(limit, 1, 10);
        var suggestions = new List<Suggestion>();
        var vehicleChips = VehicleChips(parsed);

        // Categories whose name starts with the last word ("brake" → Brakes): picked like a category tile.
        var wantParts = listingType != "Vehicle";
        if (wantParts && lastWord.Length >= 2)
        {
            foreach (var category in dictionary.Data.PartTypes.Select(p => p.Category)
                         .Where(c => !string.IsNullOrWhiteSpace(c) && Matches(c, lastWord))
                         .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c.Length).Take(take))
                suggestions.Add(new Suggestion("categories", category, [new QueryChip("category", category, category)], $"All parts in {category}"));
        }

        // Parts: the understood part type (with position / vehicle), then part types starting with the last word.
        if (wantParts && parsed.PartType is not null)
        {
            var chips = new List<QueryChip> { new("partType", parsed.PartType.Name, parsed.PartLabel()!) };
            suggestions.Add(new Suggestion("parts", Capitalise(parsed.PartLabel()!), chips, parsed.PartType.Category));
            if (parsed.VehicleLabel() is { } vehicle)
                suggestions.Add(new Suggestion("parts", $"{Capitalise(parsed.PartLabel()!)} – {vehicle}", [.. chips, .. vehicleChips], "Fits your vehicle"));
        }
        if (wantParts && lastWord.Length >= 2)
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
        // While Parts is chosen they read "Parts for Toyota Hilux 2016–2020" (group "fits").
        var vehicleGroup = listingType == "Part" ? "fits" : "vehicles";
        string VehicleLabel(string label) => listingType == "Part" ? $"Parts for {label}" : label;
        if (parsed.Make is not null && parsed.Model is not null)
            suggestions.Add(new Suggestion(vehicleGroup, VehicleLabel(await VehicleWithYearsAsync(client, parsed, listingType)), vehicleChips));
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
                if (wantParts && parsed.PartType is not null)
                {
                    chips.Insert(0, new QueryChip("partType", parsed.PartType.Name, parsed.PartLabel()!));
                    suggestions.Add(new Suggestion(vehicleGroup, $"{Capitalise(parsed.PartLabel()!)} – {label}", chips));
                }
                else suggestions.Add(new Suggestion(vehicleGroup, VehicleLabel(label), chips));
            }
        }

        // Part numbers from live listings (not while Vehicles is chosen).
        var numberQuery = Contracts.PartNumbers.Normalize(text);
        if (wantParts && numberQuery is { Length: >= 3 } && numberQuery.Any(char.IsDigit))
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
    private static async Task<string> VehicleWithYearsAsync(MeilisearchClient client, ParsedQuery parsed, string? listingType)
    {
        if (parsed.YearFrom is not null) return parsed.VehicleLabel()!;
        var key = $"{Item.Key(parsed.Make)}|{Item.Key(parsed.Model)}";
        var hits = await client.Auctions().SearchAsync<FitKeysOnly>("", new SearchQuery
        {
            Filter = $"fitKeys = {Quote(key)} AND status = \"Live\"" + (listingType is null ? "" : $" AND listingType = {Quote(listingType)}"),
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
