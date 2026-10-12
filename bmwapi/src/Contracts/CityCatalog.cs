namespace Contracts;

public record City(string Name, string CountryCode, double Latitude, double Longitude);

/// <param name="PopularCount">The first N cities are shown as "Popular cities" chips.</param>
public record CityCountry(string Code, string Name, IReadOnlyList<City> Cities, int PopularCount);

/// <summary>The place name for a GPS point: the nearest city, "Near {city}", or "Current location".</summary>
public record NearestPlace(string Name, string? CountryCode, double DistanceKm, string? CityName);

/// <summary>
/// Built-in city list (India, USA, Nigeria, Ireland) for the location screens and for naming GPS points.
/// Free and offline â€” no maps API.
/// </summary>
public static class CityCatalog
{
    public const string DefaultCountry = "IN";

    /// <summary>Further than this from the nearest city, the name becomes "Near {city}".</summary>
    public const double NearThresholdKm = 40;

    /// <summary>Further than this from every city, GPS points are just called "Current location".</summary>
    public const double FarThresholdKm = 150;

    public const string CurrentLocationName = "Current location";

    private static City C(string country, string name, double lat, double lon) => new(name, country, lat, lon);

    private static readonly Dictionary<string, CityCountry> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IN"] = new CityCountry("IN", "India",
        [
            // Popular (shown as chips, in this order)
            C("IN", "Hyderabad", 17.3850, 78.4867),
            C("IN", "Bangalore", 12.9716, 77.5946),
            C("IN", "Delhi", 28.6139, 77.2090),
            C("IN", "Mumbai", 19.0760, 72.8777),
            C("IN", "Chennai", 13.0827, 80.2707),
            C("IN", "Pune", 18.5204, 73.8567),
            // Others
            C("IN", "Kolkata", 22.5726, 88.3639),
            C("IN", "Ahmedabad", 23.0225, 72.5714),
            C("IN", "Jaipur", 26.9124, 75.7873),
            C("IN", "Visakhapatnam", 17.6868, 83.2185),
            C("IN", "Vijayawada", 16.5062, 80.6480),
            C("IN", "Warangal", 17.9689, 79.5941),
            C("IN", "Kochi", 9.9312, 76.2673),
            C("IN", "Coimbatore", 11.0168, 76.9558),
            C("IN", "Mysuru", 12.2958, 76.6394),
            C("IN", "Lucknow", 26.8467, 80.9462),
            C("IN", "Chandigarh", 30.7333, 76.7794),
            C("IN", "Nagpur", 21.1458, 79.0882),
            C("IN", "Indore", 22.7196, 75.8577),
            C("IN", "Surat", 21.1702, 72.8311),
        ], PopularCount: 6),
        ["US"] = new CityCountry("US", "United States",
        [
            C("US", "New York", 40.7128, -74.0060),
            C("US", "Los Angeles", 34.0522, -118.2437),
            C("US", "Chicago", 41.8781, -87.6298),
            C("US", "Houston", 29.7604, -95.3698),
            C("US", "Phoenix", 33.4484, -112.0740),
            C("US", "Dallas", 32.7767, -96.7970),
            C("US", "San Francisco", 37.7749, -122.4194),
            C("US", "Miami", 25.7617, -80.1918),
            C("US", "Atlanta", 33.7490, -84.3880),
            C("US", "Seattle", 47.6062, -122.3321),
        ], PopularCount: 6),
        ["NG"] = new CityCountry("NG", "Nigeria",
        [
            C("NG", "Lagos", 6.5244, 3.3792),
            C("NG", "Abuja", 9.0765, 7.3986),
            C("NG", "Kano", 12.0022, 8.5920),
            C("NG", "Ibadan", 7.3775, 3.9470),
            C("NG", "Port Harcourt", 4.8156, 7.0498),
            C("NG", "Benin City", 6.3350, 5.6037),
            C("NG", "Kaduna", 10.5105, 7.4165),
            C("NG", "Enugu", 6.4584, 7.5464),
            C("NG", "Abeokuta", 7.1475, 3.3619),
        ], PopularCount: 6),
        ["IE"] = new CityCountry("IE", "Ireland",
        [
            C("IE", "Dublin", 53.3498, -6.2603),
            C("IE", "Cork", 51.8985, -8.4756),
            C("IE", "Galway", 53.2707, -9.0568),
            C("IE", "Limerick", 52.6638, -8.6267),
            C("IE", "Waterford", 52.2593, -7.1101),
            C("IE", "Kilkenny", 52.6541, -7.2448),
            C("IE", "Drogheda", 53.7179, -6.3561),
            C("IE", "Dundalk", 54.0090, -6.4049),
            C("IE", "Sligo", 54.2766, -8.4761),
            C("IE", "Athlone", 53.4239, -7.9407),
        ], PopularCount: 6),
    };

    private static IEnumerable<City> AllCities => Countries.Values.SelectMany(c => c.Cities);

    public static bool TryGetCountry(string? code, out CityCountry country) =>
        Countries.TryGetValue(string.IsNullOrWhiteSpace(code) ? DefaultCountry : code.Trim(), out country!);

    public static IReadOnlyList<City> Popular(CityCountry country) => country.Cities.Take(country.PopularCount).ToList();

    /// <summary>
    /// Cities in all four countries matching <paramref name="query"/> (case-insensitive):
    /// names starting with it first, then the given country first, then names containing it.
    /// </summary>
    public static IReadOnlyList<City> Search(string? query, string? preferCountry = null, int limit = 10)
    {
        var q = query?.Trim();
        if (string.IsNullOrEmpty(q)) return [];

        return AllCities
            .Where(c => c.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(c => string.Equals(c.CountryCode, preferCountry, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(c => c.Name)
            .Take(limit)
            .ToList();
    }

    public static (City City, double DistanceKm) Nearest(double lat, double lon) =>
        AllCities
            .Select(c => (City: c, DistanceKm: GeoMath.HaversineKm(lat, lon, c.Latitude, c.Longitude)))
            .MinBy(x => x.DistanceKm);

    /// <summary>
    /// "Hyderabad" within 40 km, "Near Hyderabad" within 150 km, otherwise "Current location"
    /// (we keep the exact coordinates rather than naming a city that is far away).
    /// </summary>
    public static NearestPlace Describe(double lat, double lon)
    {
        var (city, distance) = Nearest(lat, lon);
        var km = Math.Round(distance, 1);
        if (distance > FarThresholdKm) return new NearestPlace(CurrentLocationName, null, km, null);
        var name = distance > NearThresholdKm ? $"Near {city.Name}" : city.Name;
        return new NearestPlace(name, city.CountryCode, km, city.Name);
    }
}
