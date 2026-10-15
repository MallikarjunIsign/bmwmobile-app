using System.Text.RegularExpressions;
using AuctionService.Data;
using AuctionService.Entities;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Services;

/// <summary>A shipping charge (or why shipping isn't possible) for one buyer location.</summary>
public record ShippingQuote(
    bool Available,
    // Whole amount in the listing currency (null when not available).
    int? Charge,
    // Road distance used (straight line × 1.3), in Unit; null when it couldn't be worked out.
    int? Distance,
    string Unit,
    string Currency,
    string SizeClass,
    // Why shipping isn't available, e.g. "Ships within India only".
    string? Reason,
    bool PlaceholderRates,
    // US: "USPS Ground Advantage" or "Freight" (too big for USPS, freight and vehicles); null elsewhere.
    string? Service = null,
    // USPS zone (1–8) and the billable weight in pounds (actual or dimensional).
    int? Zone = null,
    decimal? BillableWeightLb = null,
    // The USPS price list edition used, e.g. "2026-10-04".
    string? PriceList = null,
    // The rate is an industry-average estimate, not a carrier's price (US freight / vehicle transport for now).
    bool EstimatedRate = false);

/// <summary>Default size class of a part type (the seller can change it); vehicles are always Vehicle.</summary>
public static partial class ShippingRules
{
    private static readonly (Regex Pattern, SizeClass Size)[] Defaults =
    [
        (Freight(), SizeClass.Freight),
        (Large(), SizeClass.Large),
        (Small(), SizeClass.Small),
        (Medium(), SizeClass.Medium),
    ];

    public static SizeClass DefaultSizeClass(ListingKind? kind, string? partType)
    {
        if (kind == ListingKind.Vehicle) return SizeClass.Vehicle;
        var name = partType ?? "";
        foreach (var (pattern, size) in Defaults)
            if (pattern.IsMatch(name)) return size;
        return SizeClass.Medium;
    }

    [GeneratedRegex(@"engine assembly|^engine$|gearbox|transmission|cylinder head|differential|axle assembly", RegexOptions.IgnoreCase)]
    private static partial Regex Freight();

    [GeneratedRegex(@"bumper|bonnet|hood|^door$|door glass|boot lid|tailgate|fender|wing|dashboard|seat|windscreen|windshield|alloy wheel|^tyre|^tire|roof", RegexOptions.IgnoreCase)]
    private static partial Regex Large();

    [GeneratedRegex(@"bulb|led kit|filter|engine oil|coolant|fuse|sensor|spark plug|ecu|dash cam|brake pads|indicator|fog lamp|floor mat|jump starter|key|licence plate|wiper|car care|injector|relay", RegexOptions.IgnoreCase)]
    private static partial Regex Small();

    [GeneratedRegex(@"headlamp|tail ?lamp|radiator|condenser|compressor|alternator|starter|battery|brake disc|caliper|control arm|cv axle|clutch|catalytic|mirror|grille|shock|strut|infotainment|fuel pump", RegexOptions.IgnoreCase)]
    private static partial Regex Medium();
}

/// <summary>
/// The shipping formula (pure, unit-tested): distance = straight line (Haversine) × 1.3 road factor, in km (miles in
/// the USA); charge = base fee + distance × rate per km / mile, rounded to a whole number, kept between the size class's
/// minimum and maximum. Not available to another country or beyond the size class's maximum distance. Pickup is free.
/// </summary>
public static class ShippingCalculator
{
    public const double RoadFactor = 1.3;

    /// <summary>Road distance in the rate's unit (km or mi).</summary>
    public static double Distance(double fromLat, double fromLon, double toLat, double toLon, string unit)
    {
        var km = GeoMath.HaversineKm(fromLat, fromLon, toLat, toLon) * RoadFactor;
        return unit == "mi" ? km / RegionRules.KmPerMile : km;
    }

    /// <summary>The charge for a distance (already in the rate's unit).</summary>
    public static int Charge(ShippingRate rate, double distance)
    {
        var raw = rate.BaseFee + (decimal)distance * rate.RatePerUnit;
        var rounded = Math.Round(raw, 0, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(rounded, rate.MinFee, Math.Max(rate.MinFee, rate.MaxFee));
    }

    public static ShippingQuote Quote(ShippingRate? rate, string? itemCountry, double? itemLat, double? itemLon,
        string? buyerCountry, double buyerLat, double buyerLon, SizeClass size)
    {
        var country = itemCountry?.ToUpperInvariant();
        var unit = rate?.Unit ?? RegionRules.DistanceUnit(country);
        var currency = rate?.Currency ?? RegionRules.CurrencyFor(country);
        ShippingQuote No(string reason, int? distance = null) =>
            new(false, null, distance, unit, currency, size.ToString(), reason, rate?.IsPlaceholder ?? false);

        if (country is null || itemLat is null || itemLon is null) return No("The seller hasn't set where the item is");
        if (!string.Equals(buyerCountry, country, StringComparison.OrdinalIgnoreCase))
            return No($"Ships within {RegionRules.CountryName(country)} only");
        if (rate is null) return No("Shipping isn't set up for this item yet");

        var distance = Distance(itemLat.Value, itemLon.Value, buyerLat, buyerLon, unit);
        var whole = (int)Math.Round(distance, MidpointRounding.AwayFromZero);
        if (distance > rate.MaxDistance)
            return No($"Too far to ship: about {whole:N0} {unit} (up to {rate.MaxDistance:N0} {unit} for {size.ToString().ToLowerInvariant()} items)", whole);

        return new ShippingQuote(true, Charge(rate, distance), whole, unit, currency, size.ToString(), null, rate.IsPlaceholder,
            EstimatedRate: rate.IsEstimate);
    }
}

/// <summary>Seeds PLACEHOLDER rates per country and size class (only when missing). An admin replaces them.</summary>
public static class ShippingRateSeed
{
    /// <summary>Road miles (straight line × 1.3): coast to coast, so freight and vehicles are delivered anywhere in the US.</summary>
    public const int UsFreightMaxMiles = 3500;

    /// <summary>
    /// US freight and vehicle transport: industry averages (October 2026), labelled "estimated rate" in the app until a
    /// carrier is chosen. Fitted to base + road miles × rate from these published ranges:
    /// <list type="bullet">
    /// <item>Vehicle (open carrier, running sedan): under 500 mi about $400–1,100 (FreightWaves $1.25–2.00/mi, Ship A Car),
    ///   ~1,000 mi about $1,000 (KBB rule of thumb $1/mi; Ship A Car $1,000–1,300), coast to coast $2,200–2,800 (Ship A Car).
    ///   $350 + $0.70/mi gives 300 mi ≈ $560, 1,000 mi ≈ $1,050, New York → Los Angeles ≈ $2,580.</item>
    /// <item>Freight (LTL, one pallet up to ~500 lb, class 70): national average $532–582 all-in (Warp rate index, Aug–Sep 2026),
    ///   ~$300 for 200 mi, $400–800 for 300–1,000 mi (ParcelPath / Red Stag guides).
    ///   $250 + $0.35/mi gives 200 mi ≈ $320, 1,000 mi ≈ $600.</item>
    /// </list>
    /// </summary>
    public static readonly (SizeClass Size, decimal Base, decimal Rate, decimal Min, decimal Max, string Source)[] UsEstimates =
    [
        (SizeClass.Freight, 250, 0.35m, 250, 1500,
            "US LTL average, 1 pallet ≤500 lb (estimate): Warp LTL rate index Aug–Sep 2026 ($532–582 national average); " +
            "ParcelPath and Red Stag Fulfillment 2026 freight guides"),
        (SizeClass.Vehicle, 350, 0.70m, 400, 2800,
            "US open car transport average (estimate): FreightWaves 2026, Ship A Car 2026, Kelley Blue Book ($1/mi at ~1,000 mi), AmeriFreight"),
    ];

    // (size, base fee, rate per unit, min, max, max distance) – PLACEHOLDER numbers, not real courier prices.
    private static readonly Dictionary<string, (SizeClass Size, decimal Base, decimal Rate, decimal Min, decimal Max, int MaxDistance)[]> Placeholders = new()
    {
        // India, ₹ per km
        ["IN"] =
        [
            (SizeClass.Small, 60, 1, 60, 600, 2500), (SizeClass.Medium, 120, 2, 120, 1500, 2500),
            (SizeClass.Large, 300, 5, 300, 4000, 2000), (SizeClass.Freight, 800, 12, 800, 15000, 1500),
            (SizeClass.Vehicle, 2000, 25, 2000, 60000, 1500),
        ],
        // USA, $ per mile. Small / Medium / Large parcels are priced from the USPS Ground Advantage list
        // (UspsGroundAdvantage); these Small / Medium / Large rows are not used for US quotes. Freight and Vehicle are the
        // industry-average estimates below (UsEstimates).
        ["US"] =
        [
            (SizeClass.Small, 8, 0.15m, 8, 60, 3000), (SizeClass.Medium, 15, 0.30m, 15, 150, 3000),
            (SizeClass.Large, 40, 0.80m, 40, 400, 2500),
        ],
        // Nigeria, ₦ per km
        ["NG"] =
        [
            (SizeClass.Small, 1500, 30, 1500, 20000, 1200), (SizeClass.Medium, 3000, 60, 3000, 50000, 1200),
            (SizeClass.Large, 8000, 150, 8000, 120000, 1000), (SizeClass.Freight, 25000, 400, 25000, 400000, 800),
            (SizeClass.Vehicle, 60000, 800, 60000, 1200000, 800),
        ],
        // Ireland, € per km
        ["IE"] =
        [
            (SizeClass.Small, 6, 0.15m, 6, 40, 500), (SizeClass.Medium, 12, 0.30m, 12, 80, 500),
            (SizeClass.Large, 30, 0.80m, 30, 200, 500), (SizeClass.Freight, 90, 2.00m, 90, 600, 500),
            (SizeClass.Vehicle, 200, 2.50m, 200, 1200, 500),
        ],
    };

    public static void Seed(AuctionDbContext context)
    {
        var existing = context.ShippingRates.Select(r => new { r.CountryCode, r.SizeClass }).ToList();
        foreach (var (country, rows) in Placeholders)
        foreach (var row in rows)
        {
            if (existing.Any(e => e.CountryCode == country && e.SizeClass == row.Size)) continue;
            context.ShippingRates.Add(new ShippingRate
            {
                CountryCode = country, SizeClass = row.Size, Currency = RegionRules.CurrencyFor(country), Unit = RegionRules.DistanceUnit(country),
                BaseFee = row.Base, RatePerUnit = row.Rate, MinFee = row.Min, MaxFee = row.Max, MaxDistance = row.MaxDistance,
                IsPlaceholder = true, UpdatedBy = "seed (placeholder)",
            });
        }
        // US freight / vehicle: the industry-average estimates – added, or replacing the earlier placeholders.
        // Rows an admin has set (real carrier rates) are left alone.
        foreach (var e in UsEstimates)
        {
            var rate = context.ShippingRates.Local.FirstOrDefault(r => r.CountryCode == "US" && r.SizeClass == e.Size)
                       ?? context.ShippingRates.FirstOrDefault(r => r.CountryCode == "US" && r.SizeClass == e.Size);
            if (rate is null)
            {
                rate = new ShippingRate { CountryCode = "US", SizeClass = e.Size, Currency = "USD", Unit = "mi", IsPlaceholder = true };
                context.ShippingRates.Add(rate);
            }
            if (!rate.IsPlaceholder) continue;
            rate.BaseFee = e.Base;
            rate.RatePerUnit = e.Rate;
            rate.MinFee = e.Min;
            rate.MaxFee = e.Max;
            rate.MaxDistance = UsFreightMaxMiles;
            rate.IsPlaceholder = false;
            rate.IsEstimate = true;
            rate.Source = e.Source;
            rate.UpdatedAt = DateTime.UtcNow;
            rate.UpdatedBy = "seed (US industry-average estimate)";
        }
        context.SaveChanges();
    }

    /// <summary>Listings that offer shipping get a size class if they have none (older listings with a manual fee).</summary>
    public static void BackfillSizeClasses(AuctionDbContext context)
    {
        var items = context.Set<Item>().Include(i => i.PartType)
            .Where(i => i.SizeClass == null && (i.DeliveryOptions & DeliveryOptions.Shipping) != 0)
            .ToList();
        foreach (var item in items) item.SizeClass = ShippingRules.DefaultSizeClass(item.Kind, item.PartType?.Name);
        if (items.Count > 0) context.SaveChanges();
    }
}

/// <summary>
/// Shipping quotes: estimates on a listing and the fixed charge when an order ships.
/// <list type="bullet">
/// <item>USA, parts (Small / Medium / Large): USPS Ground Advantage retail from the official price list (zone from the
///   distance, weight / dimensional weight, oversized, non-standard fees), rounded up to a whole dollar. Parts too big
///   for USPS (over 70 lb or 130 in length + girth) go by freight.</item>
/// <item>USA, Freight and Vehicle, and every other country: the rate table (base fee + road distance × rate).</item>
/// </list>
/// </summary>
public class ShippingService(AuctionDbContext context, UspsGroundAdvantage usps)
{
    public const string FreightService = "Freight";

    public static SizeClass SizeOf(Item item) =>
        item.Kind == ListingKind.Vehicle ? SizeClass.Vehicle : item.SizeClass ?? ShippingRules.DefaultSizeClass(item.Kind, item.PartType?.Name);

    public async Task<ShippingQuote> QuoteAsync(Item item, string? buyerCountry, double buyerLat, double buyerLon, SizeClass? size = null)
    {
        var sizeClass = size ?? SizeOf(item);
        var country = item.CountryCode?.ToUpperInvariant();

        if (country == "US" && sizeClass is SizeClass.Small or SizeClass.Medium or SizeClass.Large)
        {
            var parcel = ParcelDefaults.Of(item, sizeClass);
            var quote = UspsQuote(usps, parcel, sizeClass, item.Latitude, item.Longitude, buyerCountry, buyerLat, buyerLon);
            if (quote is not null) return quote;
            // Too big for USPS: delivered by freight.
            sizeClass = SizeClass.Freight;
        }

        var rate = country is null ? null : await context.ShippingRates.AsNoTracking()
            .FirstOrDefaultAsync(r => r.CountryCode == country && r.SizeClass == sizeClass);
        var result = ShippingCalculator.Quote(rate, country, item.Latitude, item.Longitude, buyerCountry, buyerLat, buyerLon, sizeClass);
        return country == "US" ? result with { Service = FreightService } : result;
    }

    /// <summary>The USPS Ground Advantage quote; null when the parcel is too big for USPS (then freight).</summary>
    public static ShippingQuote? UspsQuote(UspsGroundAdvantage usps, Parcel parcel, SizeClass size, double? itemLat, double? itemLon,
        string? buyerCountry, double buyerLat, double buyerLon)
    {
        ShippingQuote No(string reason) => new(false, null, null, "mi", "USD", size.ToString(), reason, false, UspsGroundAdvantage.ServiceName);
        if (itemLat is null || itemLon is null) return No("The seller hasn't set where the item is");
        if (!string.Equals(buyerCountry, "US", StringComparison.OrdinalIgnoreCase)) return No("Ships within USA only");

        // USPS zones use the straight-line distance (between ZIP areas), not the road distance.
        var miles = GeoMath.HaversineKm(itemLat.Value, itemLon.Value, buyerLat, buyerLon) / RegionRules.KmPerMile;
        if (usps.Price(parcel, miles) is not { } price) return null;

        return new ShippingQuote(true, (int)Math.Ceiling(price.Price), (int)Math.Round(miles, MidpointRounding.AwayFromZero), "mi", "USD",
            size.ToString(), null, false, UspsGroundAdvantage.ServiceName, price.Zone, price.BillableWeightOz / 16m, usps.Edition);
    }
}
