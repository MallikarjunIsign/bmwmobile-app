namespace AuctionService.Services.Geocoding;

/// <summary>City and country for a GPS point. City/CountryCode are null when the provider can't tell.</summary>
public record ReverseGeocodeResult(string? City, string? CountryCode, string Source);

/// <summary>
/// Turns the phone's position into a city and country (the app's automatic location).
/// The implementation is chosen by the "Geocoding:Provider" setting: BuiltIn (default, offline) or Google.
/// </summary>
public interface IReverseGeocoder
{
    Task<ReverseGeocodeResult> ReverseAsync(double latitude, double longitude, CancellationToken ct = default);
}
