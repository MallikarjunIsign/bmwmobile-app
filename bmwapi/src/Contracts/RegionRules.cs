namespace Contracts;

/// <summary>A search radius in the country's unit (miles in the USA, km elsewhere) and as km.</summary>
public record SearchRadius(int Value, string Unit, double Km);

/// <summary>Per-country rules shared by the services (and sent to the app, so it never hard-codes them).</summary>
public static class RegionRules
{
    public const double KmPerMile = 1.609344;

    /// <summary>Countries the marketplace runs in (units and currency are set up for these).</summary>
    public static readonly IReadOnlySet<string> SupportedCountries =
        new HashSet<string>(["IN", "US", "NG", "IE"], StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string? countryCode) =>
        !string.IsNullOrWhiteSpace(countryCode) && SupportedCountries.Contains(countryCode);

    /// <summary>Listing currency: INR, USD, NGN, EUR (amounts are never converted).</summary>
    public static string CurrencyFor(string? countryCode) => countryCode?.ToUpperInvariant() switch
    {
        "US" => "USD",
        "NG" => "NGN",
        "IE" => "EUR",
        _ => "INR"
    };

    /// <summary>"India", "USA", "Nigeria", "Ireland" (for messages such as "Ships within India").</summary>
    public static string CountryName(string? countryCode) => countryCode?.ToUpperInvariant() switch
    {
        "US" => "USA",
        "NG" => "Nigeria",
        "IE" => "Ireland",
        "IN" => "India",
        _ => countryCode ?? ""
    };

    /// <summary>Mileage and distances: miles in the USA, km elsewhere.</summary>
    public static string DistanceUnit(string? countryCode) =>
        string.Equals(countryCode, "US", StringComparison.OrdinalIgnoreCase) ? "mi" : "km";

    // ── The search radius: THE one setting for every service (the app has the same numbers in utils/geo.ts). ──
    // Values are in the location's unit: 200 miles in the USA, 200 km elsewhere.
    public const int DefaultRadiusValue = 200;
    public const int MinRadiusValue = 5;
    public const int MaxRadiusValue = 1000;

    /// <summary>The largest radius in km (1,000 miles).</summary>
    public static readonly double MaxRadiusKm = Math.Round(MaxRadiusValue * KmPerMile, 2);

    /// <summary>A radius in the country's unit: 200 → 200 mi (321.87 km) in the USA, 200 km elsewhere.</summary>
    public static SearchRadius Radius(int value, string? countryCode) =>
        DistanceUnit(countryCode) == "mi"
            ? new SearchRadius(value, "mi", Math.Round(value * KmPerMile, 2))
            : new SearchRadius(value, "km", value);

    /// <summary>The value in the country's unit for a radius in km (321.87 km → 200 in the USA).</summary>
    public static int RadiusValue(double km, string? countryCode) =>
        (int)Math.Round(DistanceUnit(countryCode) == "mi" ? km / KmPerMile : km, MidpointRounding.AwayFromZero);

    public static bool IsValidRadius(int value) => value is >= MinRadiusValue and <= MaxRadiusValue;

    /// <summary>USA → 200 miles; India, Nigeria, Ireland (and anything else) → 200 km.</summary>
    public static SearchRadius DefaultSearchRadius(string? countryCode) => Radius(DefaultRadiusValue, countryCode);
}
