using Contracts;

namespace AuctionService.Services.Geocoding;

/// <summary>
/// Offline reverse geocoding with the built-in city list (India, USA, Nigeria, Ireland): the nearest listed city
/// within 150 km and its country. Free and needs no key, but coarse – use the Google provider in production.
/// </summary>
public class BuiltInReverseGeocoder : IReverseGeocoder
{
    public Task<ReverseGeocodeResult> ReverseAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        var place = CityCatalog.Describe(latitude, longitude);
        return Task.FromResult(new ReverseGeocodeResult(place.CityName, place.CountryCode, "BuiltIn"));
    }
}
