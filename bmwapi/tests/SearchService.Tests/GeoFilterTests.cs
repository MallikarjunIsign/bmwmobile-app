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
    public void Near_a_pole_only_latitude_is_filtered()
    {
        var filter = SearchEndpoints.BoundingBoxFilter(GeoMath.BoundingBox(89.9, 10, 50));

        Assert.Contains("longitude EXISTS", filter);
        Assert.DoesNotContain("longitude >=", filter);
    }
}
