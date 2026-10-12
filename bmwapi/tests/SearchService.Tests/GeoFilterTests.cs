using Contracts;
using SearchService.Endpoints;

namespace SearchService.Tests;

public class GeoFilterTests
{
    [Fact]
    public void BoundingBox_filter_uses_invariant_numbers_and_both_ranges()
    {
        var filter = SearchEndpoints.BoundingBoxFilter(GeoMath.BoundingBox(17.4849, 78.4138, 25));

        Assert.StartsWith("(latitude >= 17.", filter);
        Assert.Contains("longitude >= 78.", filter);
        Assert.Contains("longitude <= 78.", filter);
        Assert.DoesNotContain(",", filter); // no culture-specific decimal commas
    }

    [Fact]
    public void Country_filter_is_an_OR_of_the_country_boxes()
    {
        Assert.Equal(3, SearchEndpoints.CountryFilter("us")!.Split(" OR ").Length);
        Assert.StartsWith("((latitude >= 6.5", SearchEndpoints.CountryFilter("IN"));
        Assert.Null(SearchEndpoints.CountryFilter("XX"));
        Assert.Null(SearchEndpoints.CountryFilter(null));
    }

    [Theory]
    [InlineData("IN", 17.385, 78.4867, true)]   // Hyderabad
    [InlineData("US", 40.71, -74.0, true)]      // New York
    [InlineData("NG", 6.5244, 3.3792, true)]    // Lagos
    [InlineData("IE", 53.3498, -6.2603, true)]  // Dublin
    [InlineData("IE", 17.385, 78.4867, false)]
    public void Country_bounds_contain_their_cities(string country, double lat, double lon, bool expected) =>
        Assert.Equal(expected, CountryBounds.Contains(country, lat, lon));

    [Fact]
    public void Near_a_pole_only_latitude_is_filtered()
    {
        var filter = SearchEndpoints.BoundingBoxFilter(GeoMath.BoundingBox(89.9, 10, 50));

        Assert.Contains("longitude EXISTS", filter);
        Assert.DoesNotContain("longitude >=", filter);
    }
}
