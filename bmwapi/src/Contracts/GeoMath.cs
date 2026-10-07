namespace Contracts;

/// <summary>Min/max latitude and longitude around a point (degrees).</summary>
public readonly record struct GeoBox(double MinLatitude, double MaxLatitude, double MinLongitude, double MaxLongitude)
{
    public bool Contains(double lat, double lon) =>
        lat >= MinLatitude && lat <= MaxLatitude && lon >= MinLongitude && lon <= MaxLongitude;
}

/// <summary>
/// Distance helpers for "near me" features (user locations, parts within X km).
/// Pure maths, no external services. Shared here so every service uses the same formulas.
/// </summary>
public static class GeoMath
{
    public const double EarthRadiusKm = 6371.0;

    /// <summary>Great-circle distance between two points in km.</summary>
    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    /// <summary>
    /// A box that contains every point within <paramref name="radiusKm"/> of the centre.
    /// Use it as a cheap pre-filter (lat/lon ranges), then check the exact distance with
    /// <see cref="HaversineKm"/>. Near the poles or across the 180° line the box widens to
    /// all longitudes, so it never drops a real match.
    /// </summary>
    public static GeoBox BoundingBox(double lat, double lon, double radiusKm)
    {
        var latDelta = ToDegrees(radiusKm / EarthRadiusKm);
        var minLat = lat - latDelta;
        var maxLat = lat + latDelta;

        if (minLat <= -90 || maxLat >= 90)
            return new GeoBox(Math.Max(minLat, -90), Math.Min(maxLat, 90), -180, 180);

        var lonDelta = ToDegrees(Math.Asin(Math.Min(1, Math.Sin(radiusKm / EarthRadiusKm) / Math.Cos(ToRadians(lat)))));
        var minLon = lon - lonDelta;
        var maxLon = lon + lonDelta;

        if (minLon < -180 || maxLon > 180)
            return new GeoBox(minLat, maxLat, -180, 180);

        return new GeoBox(minLat, maxLat, minLon, maxLon);
    }

    /// <summary>Latitude −90..90, longitude −180..180, and not exactly (0,0) — the usual "no GPS fix" value.</summary>
    public static bool IsValidCoordinate(double lat, double lon) =>
        double.IsFinite(lat) && double.IsFinite(lon) &&
        lat is >= -90 and <= 90 &&
        lon is >= -180 and <= 180 &&
        !(lat == 0 && lon == 0);

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
    private static double ToDegrees(double radians) => radians * 180 / Math.PI;
}
