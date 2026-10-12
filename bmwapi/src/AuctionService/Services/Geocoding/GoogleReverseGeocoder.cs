using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AuctionService.Services.Geocoding;

/// <summary>
/// Google Geocoding API (reverse). Needs Geocoding:Google:ApiKey from an environment variable or user-secrets.
/// Takes the "locality" (else the nearest administrative area) as the city and the country's ISO code.
/// </summary>
public class GoogleReverseGeocoder(HttpClient http, IOptions<GeocodingOptions> options, ILogger<GoogleReverseGeocoder> logger)
    : IReverseGeocoder
{
    public async Task<ReverseGeocodeResult> ReverseAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        var google = options.Value.Google;
        if (string.IsNullOrWhiteSpace(google.ApiKey))
            throw new InvalidOperationException(
                "Geocoding:Provider is Google but Geocoding:Google:ApiKey is not set (use an environment variable or user-secrets)");

        var url = string.Create(CultureInfo.InvariantCulture,
            $"{google.BaseUrl}?latlng={latitude},{longitude}&language=en&key={Uri.EscapeDataString(google.ApiKey)}");

        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var result = Parse(json.RootElement);
        if (result.City is null && result.CountryCode is null)
            logger.LogInformation("Google geocoding found no city/country (status {Status})",
                json.RootElement.TryGetProperty("status", out var s) ? s.GetString() : "?");
        return result;
    }

    /// <summary>Reads city and country from a Google Geocoding response (public for tests).</summary>
    public static ReverseGeocodeResult Parse(JsonElement root)
    {
        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return new ReverseGeocodeResult(null, null, "Google");

        string? city = null, fallbackCity = null, country = null;
        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("address_components", out var components)) continue;
            foreach (var component in components.EnumerateArray())
            {
                var types = component.GetProperty("types").EnumerateArray().Select(t => t.GetString()).ToList();
                if (types.Contains("locality")) city ??= component.GetProperty("long_name").GetString();
                else if (types.Contains("postal_town") || types.Contains("administrative_area_level_2"))
                    fallbackCity ??= component.GetProperty("long_name").GetString();
                if (types.Contains("country")) country ??= component.GetProperty("short_name").GetString();
            }
            if (city != null && country != null) break;
        }

        return new ReverseGeocodeResult(city ?? fallbackCity, country?.ToUpperInvariant(), "Google");
    }
}
