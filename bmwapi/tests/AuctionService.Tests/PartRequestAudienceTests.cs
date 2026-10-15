using Contracts;

namespace AuctionService.Tests;

/// <summary>Which sellers hear about a buyer's part request: the buyer's radius, or their country – never another country.</summary>
public class PartRequestAudienceTests
{
    // Buyer in Kukatpally, Hyderabad, 60 km radius.
    private const double BuyerLat = 17.4849, BuyerLon = 78.4138;

    private static bool Reaches(string? sellerCountry, double? lat, double? lon, double? radius = 60, bool buyerLocation = true) =>
        PartRequestAudience.Reaches("IN", buyerLocation ? BuyerLat : null, buyerLocation ? BuyerLon : null, buyerLocation ? radius : null,
            sellerCountry, lat, lon);

    [Fact]
    public void BuyerWithLocation_SellersWithinTheRadiusOnly()
    {
        Assert.True(Reaches("IN", 17.4399, 78.4983));    // Secunderabad, ~10 km
        Assert.False(Reaches("IN", 16.5062, 80.6480));   // Vijayawada, ~250 km
        Assert.True(Reaches("IN", 16.5062, 80.6480, radius: 300)); // the buyer's own radius counts
        Assert.False(Reaches("IN", null, null));         // seller location unknown: can't tell
    }

    [Fact]
    public void NeverAnotherCountry_EvenWhenClose()
    {
        // A seller with the right coordinates but another country (e.g. a stale GPS fix) is still left out.
        Assert.False(Reaches("NG", 17.4399, 78.4983));
        Assert.False(Reaches("NG", 6.5244, 3.3792, buyerLocation: false));
    }

    [Fact]
    public void BuyerWithoutLocation_SellersInTheSameCountry()
    {
        Assert.True(Reaches("IN", 28.6139, 77.2090, buyerLocation: false)); // Delhi: same country, distance doesn't matter
        Assert.True(Reaches("in", null, null, buyerLocation: false));
        Assert.False(Reaches(null, 28.6139, 77.2090, buyerLocation: false)); // seller's country unknown
    }

    [Fact]
    public void DefaultRadius_Is200Km()
    {
        // Warangal (~140 km): inside the default 200 km when the buyer's radius is missing; Vijayawada (~250 km) isn't.
        Assert.True(PartRequestAudience.Reaches("IN", BuyerLat, BuyerLon, null, "IN", 18.0072, 79.5584));
        Assert.False(PartRequestAudience.Reaches("IN", BuyerLat, BuyerLon, null, "IN", 16.5062, 80.6480));
    }
}
