namespace Contracts;

/// <summary>Default search radius for a country: 60 miles in the USA, 60 km elsewhere.</summary>
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

    /// <summary>USA → 60 miles; India, Nigeria, Ireland (and anything else) → 60 km.</summary>
    public static SearchRadius DefaultSearchRadius(string? countryCode) =>
        string.Equals(countryCode, "US", StringComparison.OrdinalIgnoreCase)
            ? new SearchRadius(60, "mi", Math.Round(60 * KmPerMile, 2))
            : new SearchRadius(60, "km", 60);
}
