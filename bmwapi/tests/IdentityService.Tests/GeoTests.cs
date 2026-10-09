using Contracts;
using IdentityService.Options;
using IdentityService.Services;

namespace IdentityService.Tests;

public class GeoMathTests
{
    private const double HydLat = 17.3850, HydLon = 78.4867;
    private const double BlrLat = 12.9716, BlrLon = 77.5946;

    [Fact]
    public void Haversine_HyderabadToBangalore_IsAbout500Km() =>
        Assert.InRange(GeoMath.HaversineKm(HydLat, HydLon, BlrLat, BlrLon), 490, 510);

    [Fact]
    public void Haversine_LagosToAbuja_IsAbout525Km() =>
        Assert.InRange(GeoMath.HaversineKm(6.5244, 3.3792, 9.0765, 7.3986), 510, 540);

    [Fact]
    public void Haversine_SamePoint_IsZero() =>
        Assert.Equal(0, GeoMath.HaversineKm(HydLat, HydLon, HydLat, HydLon), precision: 9);

    [Fact]
    public void BoundingBox_ContainsEveryPointOnTheRadiusCircle()
    {
        foreach (var (lat, lon) in new[] { (HydLat, HydLon), (6.5244, 3.3792), (47.6062, -122.3321) })
        {
            const double radius = 100;
            var box = GeoMath.BoundingBox(lat, lon, radius);
            for (var bearing = 0; bearing < 360; bearing += 5)
            {
                var (pLat, pLon) = Destination(lat, lon, bearing, radius * 0.999);
                Assert.True(box.Contains(pLat, pLon), $"bearing {bearing} from ({lat},{lon}) is outside the box");
            }
        }
    }

    [Fact]
    public void BoundingBox_ExcludesFarAwayPoints() =>
        Assert.False(GeoMath.BoundingBox(HydLat, HydLon, 50).Contains(BlrLat, BlrLon));

    [Fact]
    public void BoundingBox_NearPole_UsesAllLongitudes()
    {
        var box = GeoMath.BoundingBox(89.9, 10, 50);
        Assert.Equal(-180, box.MinLongitude);
        Assert.Equal(180, box.MaxLongitude);
    }

    [Theory]
    [InlineData(17.385, 78.4867, true)]
    [InlineData(0, 0, false)]
    [InlineData(91, 78, false)]
    [InlineData(17, 181, false)]
    [InlineData(double.NaN, 78, false)]
    public void IsValidCoordinate(double lat, double lon, bool expected) =>
        Assert.Equal(expected, GeoMath.IsValidCoordinate(lat, lon));

    private static (double Lat, double Lon) Destination(double lat, double lon, double bearingDeg, double km)
    {
        var d = km / GeoMath.EarthRadiusKm;
        var b = bearingDeg * Math.PI / 180;
        var lat1 = lat * Math.PI / 180;
        var lon1 = lon * Math.PI / 180;
        var lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(d) + Math.Cos(lat1) * Math.Sin(d) * Math.Cos(b));
        var lon2 = lon1 + Math.Atan2(Math.Sin(b) * Math.Sin(d) * Math.Cos(lat1), Math.Cos(d) - Math.Sin(lat1) * Math.Sin(lat2));
        return (lat2 * 180 / Math.PI, lon2 * 180 / Math.PI);
    }
}

public class CityCatalogTests
{
    [Theory]
    [InlineData(null, "Hyderabad,Bangalore,Delhi,Mumbai,Chennai,Pune")]
    [InlineData("US", "New York,Los Angeles,Chicago,Houston,Phoenix,Dallas")]
    [InlineData("ng", "Lagos,Abuja,Kano,Ibadan,Port Harcourt,Benin City")]
    [InlineData("IE", "Dublin,Cork,Galway,Limerick,Waterford,Kilkenny")]
    public void Popular_IsFirstSixInOrder(string? country, string expected)
    {
        Assert.True(CityCatalog.TryGetCountry(country, out var c));
        Assert.Equal(expected, string.Join(",", CityCatalog.Popular(c).Select(x => x.Name)));
    }

    [Fact]
    public void Unknown_Country_IsRejected() => Assert.False(CityCatalog.TryGetCountry("XX", out _));

    [Theory]
    [InlineData(17.4849, 78.4138, "Hyderabad", "IN")]   // Kukatpally
    [InlineData(6.6018, 3.3515, "Lagos", "NG")]         // Ikeja
    [InlineData(40.7306, -73.9352, "New York", "US")]   // Brooklyn
    [InlineData(53.2895, -6.1350, "Dublin", "IE")]      // Dún Laoghaire
    public void Describe_CloseToACity_UsesItsName(double lat, double lon, string name, string country)
    {
        var place = CityCatalog.Describe(lat, lon);
        Assert.Equal(name, place.Name);
        Assert.Equal(country, place.CountryCode);
    }

    [Fact]
    public void Describe_40To150Km_SaysNear() =>
        Assert.Equal("Near Hyderabad", CityCatalog.Describe(17.62, 78.08).Name);

    [Theory]
    [InlineData(15.8281, 78.0373)] // Kurnool, ~180 km from the nearest catalog city
    [InlineData(48.8566, 2.3522)]  // Paris
    public void Describe_Over150Km_IsCurrentLocation(double lat, double lon)
    {
        var place = CityCatalog.Describe(lat, lon);
        Assert.Equal(CityCatalog.CurrentLocationName, place.Name);
        Assert.Null(place.CountryCode);
    }

    [Fact]
    public void Search_CoversAllCountries_StartsWithFirst()
    {
        var names = CityCatalog.Search("ka").Select(c => c.Name).ToList();
        Assert.Contains("Kano", names);
        Assert.Contains("Kaduna", names);
        Assert.Contains("Kolkata", names);
        Assert.True(names.IndexOf("Kano") < names.IndexOf("Kolkata"));
        Assert.Equal("Lagos", Assert.Single(CityCatalog.Search("LAG")).Name);
        Assert.Empty(CityCatalog.Search("  "));
    }

    [Fact]
    public void Search_PrefersTheGivenCountry()
    {
        Assert.Equal("Abeokuta", CityCatalog.Search("a", "NG").First().Name);
        Assert.Equal("Ahmedabad", CityCatalog.Search("a", "IN").First().Name);
        Assert.Equal("Athlone", CityCatalog.Search("a", "IE").First().Name);
    }
}

public class LocationOptionsTests
{
    [Theory]
    [InlineData(null, 50)]
    [InlineData(25, 25)]
    [InlineData(1, 5)]
    [InlineData(999, 200)]
    public void Radius_DefaultsAndClamps(int? input, int expected) =>
        Assert.Equal(expected, new LocationOptions().ClampRadius(input));
}
