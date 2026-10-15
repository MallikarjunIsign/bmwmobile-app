using System.Globalization;
using System.Text.Json.Serialization;
using Contracts;

namespace SearchService.Models;

/// <summary>
/// Meilisearch document (mirrors AuctionService's public AuctionDto). Deliberately has no ReservePrice:
/// the reserve is never indexed; only ReserveMet is.
/// </summary>
public class Item
{
    public required string Id { get; set; }
    public int StartingPrice { get; set; }
    public int BidIncrement { get; set; }
    public bool ReserveMet { get; set; }
    public required string Seller { get; set; }
    public string? Winner { get; set; }
    public int? SoldAmount { get; set; }
    public int? CurrentHighBid { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime AuctionEnd { get; set; }
    public required string Status { get; set; }
    public string? EndReason { get; set; }
    public DateTime? SellerDecisionDeadline { get; set; }

    public string ItemType { get; set; } = "Vehicle";
    public required string Make { get; set; }
    public required string Model { get; set; }
    public int Year { get; set; }
    public int? YearTo { get; set; }
    public string? Color { get; set; }
    public int? Mileage { get; set; }
    public required string Description { get; set; }
    public string? PartName { get; set; }
    public string? PartNumber { get; set; }
    public string? PartCategory { get; set; }
    public string? Condition { get; set; }
    public int? Quantity { get; set; }

    public ContainerInfo? Container { get; set; }

    // Where the item is (null for older listings). Filterable, for "parts within X km".
    public string? LocationName { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public string ImageUrl { get; set; } = string.Empty;
    public List<string> Images { get; set; } = [];

    // ── Listing model (design doc §7.4) ──
    /// <summary>CommonPart, VehicleSpecificPart or Vehicle.</summary>
    public string? Kind { get; set; }
    /// <summary>Universal, SizeBased or VehicleSpecific.</summary>
    public string? FitmentMode { get; set; }
    public int? PartTypeId { get; set; }
    public string? PartType { get; set; }
    public string? Category { get; set; }
    public string? Title { get; set; }
    public string? FitmentSummary { get; set; }
    public List<FitmentInfo> Fitments { get; set; } = [];
    public List<string> PartNumbers { get; set; } = [];
    public Dictionary<string, string> Specs { get; set; } = [];
    public string? BrandType { get; set; }
    public string? Brand { get; set; }
    public string? Position { get; set; }
    public string? Side { get; set; }
    public string? UsedGrade { get; set; }
    public string? CountryCode { get; set; }
    public string? Currency { get; set; }
    /// <summary>Pickup and / or Shipping. Filterable ("Delivery" filter).</summary>
    public List<string> DeliveryOptions { get; set; } = [];
    /// <summary>Shipping fee (0 = free), null without shipping.</summary>
    public int? ShippingFee { get; set; }
    /// <summary>Pickup city ("Pickup in Hyderabad").</summary>
    public string? PickupCity { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int BidCount { get; set; }
    public VehicleSummary? Vehicle { get; set; }
    /// <summary>Auction or FixedPrice. Filterable.</summary>
    public string SaleType { get; set; } = "Auction";
    /// <summary>The seller's display name (null on older listings: show Seller).</summary>
    public string? SellerName { get; set; }
    /// <summary>FixedPrice: units still for sale.</summary>
    public int QuantityAvailable { get; set; }
    /// <summary>The seller's average rating (null: no reviews yet) and number of ratings. Filterable and sortable.</summary>
    public double? SellerRating { get; set; }
    public int SellerRatingCount { get; set; }

    // ── Computed for filtering / ranking (see Prepare) ──
    /// <summary>Bumped when computed fields are added: older documents are re-fetched at startup (DbInitializer).</summary>
    public const int CurrentIndexVersion = 2;
    public int IndexVersion { get; set; }

    /// <summary>Meilisearch geo point: exact "within X km" filtering (_geoRadius) so facet counts match the radius.</summary>
    [JsonPropertyName("_geo")]
    public GeoPoint? Geo { get; set; }

    /// <summary>"Part" or "Vehicle" (Home's Parts / Vehicles switch). From Kind, or ItemType on older listings.</summary>
    public string ListingType { get; set; } = "Part";

    /// <summary>Every make / "Make|Model" / year the item is for (fitment rows, or the vehicle itself). Facets.</summary>
    public List<string> Makes { get; set; } = [];
    public List<string> Models { get; set; } = [];
    public List<string> Years { get; set; } = [];

    /// <summary>"toyota", "toyota|hilux", "toyota|hilux|2016" … one key per year of every fitment row.</summary>
    public List<string> FitKeys { get; set; } = [];
    /// <summary>Part numbers uppercased without spaces / dashes.</summary>
    public List<string> PartNumbersNormalized { get; set; } = [];
    /// <summary>"width:265", "rim:17", "tyresize:265/65r17" …</summary>
    public List<string> SpecKeys { get; set; } = [];
    /// <summary>What a buyer pays now: the highest bid total, or the part price.</summary>
    public int CurrentPrice { get; set; }

    /// <summary>Fills the computed fields. Call before every write to the index.</summary>
    public Item Prepare()
    {
        var keys = new HashSet<string>();
        void AddVehicle(string make, string model, int from, int to)
        {
            var mk = Key(make);
            if (mk.Length == 0) return;
            keys.Add(mk);
            var md = Key(model);
            if (md.Length == 0) return;
            keys.Add($"{mk}|{md}");
            if (from <= 0) return;
            for (var y = from; y <= Math.Max(from, to) && y - from < 60; y++)
                keys.Add($"{mk}|{md}|{y}");
        }

        foreach (var f in Fitments) AddVehicle(f.Make, f.Model, f.YearFrom, f.YearTo);
        // Older listings (no fitment rows) and whole vehicles use the item's make/model/years.
        if (Fitments.Count == 0 && FitmentMode is not ("Universal" or "SizeBased"))
            AddVehicle(Make, Model, Year, YearTo ?? Year);
        FitKeys = keys.ToList();

        Makes = Fitments.Count > 0 ? Fitments.Select(f => f.Make).Where(m => m.Length > 0).Distinct().ToList()
            : Make.Length > 0 ? [Make] : [];
        Models = Fitments.Count > 0 ? Fitments.Where(f => f.Make.Length > 0 && f.Model.Length > 0).Select(f => $"{f.Make}|{f.Model}").Distinct().ToList()
            : Make.Length > 0 && Model.Length > 0 ? [$"{Make}|{Model}"] : [];
        Years = FitKeys.Select(k => k.Split('|')).Where(p => p.Length == 3).Select(p => p[2]).Distinct().ToList();

        ListingType = Kind switch
        {
            "Vehicle" => "Vehicle",
            "CommonPart" or "VehicleSpecificPart" => "Part",
            _ => ItemType == "Part" ? "Part" : "Vehicle",
        };
        Geo = Latitude is { } lat && Longitude is { } lon ? new GeoPoint(lat, lon) : null;
        IndexVersion = CurrentIndexVersion;

        var numbers = PartNumbers.Count > 0 ? PartNumbers : string.IsNullOrWhiteSpace(PartNumber) ? [] : [PartNumber];
        PartNumbersNormalized = numbers.Select(Contracts.PartNumbers.Normalize).OfType<string>().Distinct().ToList();

        var specs = Specs.Select(s => $"{s.Key.ToLowerInvariant()}:{s.Value.Trim().ToLowerInvariant()}").ToList();
        if (Specs.TryGetValue("width", out var w) && Specs.TryGetValue("profile", out var p) && Specs.TryGetValue("rim", out var r))
            specs.Add($"tyresize:{TyreSize(w, p, r)}");
        SpecKeys = specs;

        CurrentPrice = CurrentHighBid ?? StartingPrice;
        Title ??= ItemType == "Part" && !string.IsNullOrWhiteSpace(PartName)
            ? $"{PartName} (fits {Make} {Model})"
            : $"{Year} {Make} {Model}";
        return this;
    }

    public static string Key(string? value) => (value ?? "").Trim().ToLowerInvariant();

    public static string TyreSize(string width, string profile, string rim) =>
        string.Create(CultureInfo.InvariantCulture, $"{width.Trim()}/{profile.Trim()}r{rim.Trim().TrimStart('R', 'r')}").ToLowerInvariant();
}

public record GeoPoint(double Lat, double Lng);

/// <summary>A search hit plus its distance from the searcher (only when lat/lon were given).</summary>
public class SearchResultItem : Item
{
    public double? DistanceKm { get; set; }
    /// <summary>"Fits your 2016 Toyota Hilux", "Universal", "Fits by size" or "Check fitment".</summary>
    public string? Badge { get; set; }
    /// <summary>0 part number · 1 exact vehicle + part type · 2 make + model · 3 universal / size · 4 keyword only.</summary>
    public int MatchTier { get; set; }
}
