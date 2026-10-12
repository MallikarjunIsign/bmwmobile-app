namespace Contracts;

/// <summary>
/// Rough latitude/longitude boxes for the supported countries, for "country-wide" results when the user's location
/// is unknown (location off). Listings are matched by their coordinates; border areas may include a little of a
/// neighbouring country.
/// </summary>
public static class CountryBounds
{
    private static readonly Dictionary<string, GeoBox[]> Boxes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IN"] = [new GeoBox(6.5, 35.7, 68.1, 97.4)],
        ["US"] =
        [
            new GeoBox(24.4, 49.4, -125.0, -66.9),   // contiguous states
            new GeoBox(51.0, 71.5, -170.0, -129.9),  // Alaska
            new GeoBox(18.9, 22.3, -160.3, -154.8),  // Hawaii
        ],
        ["NG"] = [new GeoBox(4.2, 13.9, 2.6, 14.7)],
        ["IE"] = [new GeoBox(51.4, 55.4, -10.7, -5.9)],
    };

    public static bool TryGet(string? countryCode, out GeoBox[] boxes)
    {
        boxes = [];
        return !string.IsNullOrWhiteSpace(countryCode) && Boxes.TryGetValue(countryCode.Trim(), out boxes!);
    }

    public static bool Contains(string countryCode, double latitude, double longitude) =>
        TryGet(countryCode, out var boxes) && boxes.Any(b =>
            latitude >= b.MinLatitude && latitude <= b.MaxLatitude &&
            longitude >= b.MinLongitude && longitude <= b.MaxLongitude);
}
