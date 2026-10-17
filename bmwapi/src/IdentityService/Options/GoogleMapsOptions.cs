namespace IdentityService.Options;

/// <summary>
/// "GoogleMaps" section: location search (Places API (New) autocomplete + details) and reverse geocoding
/// (Geocoding API) for /api/places. The API key is read ONLY from user-secrets or an environment variable
/// (GoogleMaps:ApiKey / GoogleMaps__ApiKey) – never put it in appsettings. No key = the built-in city list.
/// </summary>
public class GoogleMapsOptions
{
    public const string SectionName = "GoogleMaps";

    public string? ApiKey { get; set; }

    public string PlacesBaseUrl { get; set; } = "https://places.googleapis.com/v1";
    public string GeocodingUrl { get; set; } = "https://maps.googleapis.com/maps/api/geocode/json";

    /// <summary>Results are restricted to the countries we operate in (lower-case CLDR region codes).</summary>
    public string[] RegionCodes { get; set; } = ["in", "us", "ng", "ie"];

    public string Language { get; set; } = "en";

    /// <summary>Suggestions are biased to this circle around the user's location (Google allows at most 50,000 m).</summary>
    public double BiasRadiusMeters { get; set; } = 50_000;

    /// <summary>Per user: autocomplete calls per minute and per day (UTC).</summary>
    public int AutocompletePerMinute { get; set; } = 30;
    public int AutocompletePerDay { get; set; } = 300;

    /// <summary>Per user: place details + "use my current location" lookups per minute and per day (UTC).</summary>
    public int LookupsPerMinute { get; set; } = 30;
    public int LookupsPerDay { get; set; } = 300;

    /// <summary>All Google calls of the whole service per day (UTC). Above it: the built-in city list until midnight UTC.</summary>
    public int DailyCallLimit { get; set; } = 5_000;

    /// <summary>Place details (by place ID) and reverse geocoding (by rounded point) are cached this long.</summary>
    public double CacheDays { get; set; } = 30;

    /// <summary>Reverse geocoding rounds the point to this many decimals for the cache (3 ≈ 110 m).</summary>
    public int ReverseRoundDecimals { get; set; } = 3;

    public int TimeoutSeconds { get; set; } = 8;
}
