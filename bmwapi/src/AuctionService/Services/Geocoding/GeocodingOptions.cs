namespace AuctionService.Services.Geocoding;

/// <summary>
/// "Geocoding" section. Provider: "BuiltIn" (offline city list for India, USA, Nigeria, Ireland – no key needed)
/// or "Google" (Google Geocoding API). The Google API key is read ONLY from environment variables or
/// user-secrets (Geocoding__Google__ApiKey / "Geocoding:Google:ApiKey"); never put it in appsettings.
/// </summary>
public class GeocodingOptions
{
    public const string SectionName = "Geocoding";

    public string Provider { get; set; } = "BuiltIn";

    /// <summary>Results are cached per point rounded to 2 decimals (about 1 km) for this long.</summary>
    public double CacheHours { get; set; } = 24;

    public GoogleGeocodingOptions Google { get; set; } = new();
}

public class GoogleGeocodingOptions
{
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://maps.googleapis.com/maps/api/geocode/json";
}
