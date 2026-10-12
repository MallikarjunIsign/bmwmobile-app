using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AuctionService.Services.Geocoding;

/// <summary>
/// Caches another geocoder's answers per point rounded to 2 decimals (about 1 km), so phones that open the app
/// in the same area don't call the (possibly paid) provider again.
/// </summary>
public class CachedReverseGeocoder(IReverseGeocoder inner, IMemoryCache cache, IOptions<GeocodingOptions> options)
    : IReverseGeocoder
{
    public async Task<ReverseGeocodeResult> ReverseAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        var key = CacheKey(latitude, longitude);
        if (cache.TryGetValue(key, out ReverseGeocodeResult? cached) && cached is not null) return cached;

        var result = await inner.ReverseAsync(Math.Round(latitude, 2), Math.Round(longitude, 2), ct);
        cache.Set(key, result, TimeSpan.FromHours(Math.Max(options.Value.CacheHours, 0.01)));
        return result;
    }

    public static string CacheKey(double latitude, double longitude) =>
        string.Create(CultureInfo.InvariantCulture, $"geo:{Math.Round(latitude, 2):F2},{Math.Round(longitude, 2):F2}");
}
