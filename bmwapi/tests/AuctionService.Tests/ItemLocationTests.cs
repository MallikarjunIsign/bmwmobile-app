using System.Security.Claims;
using AuctionService.Entities;
using AuctionService.RequestHelpers;
using AuctionService.Validation;

namespace AuctionService.Tests;

public class ItemLocationTests
{
    private static Auction NewPart(double? lat = null, double? lon = null) => new()
    {
        Seller = "seller",
        StartingPrice = 100,
        ReservePrice = 100,
        BidIncrement = 10,
        AuctionEnd = DateTime.UtcNow.AddDays(3),
        Status = Status.Live,
        Item = new Item
        {
            ItemType = ItemType.Part,
            Make = "BMW",
            Model = "X5",
            Year = 2020,
            Description = "Test",
            PartName = "Mirror",
            PartCategory = PartCategory.Body,
            Condition = ItemCondition.Used,
            Quantity = 1,
            Latitude = lat,
            Longitude = lon
        }
    };

    private static ClaimsPrincipal Seller(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    [Fact]
    public void Location_is_optional() => Assert.Empty(AuctionValidator.Validate(NewPart(), requireFutureEnd: true));

    [Fact]
    public void Valid_location_passes() =>
        Assert.Empty(AuctionValidator.Validate(NewPart(6.5244, 3.3792), requireFutureEnd: true));

    [Theory]
    [InlineData(6.5244, null)]
    [InlineData(0.0, 0.0)]
    [InlineData(95.0, 3.0)]
    public void Invalid_location_is_rejected(double? lat, double? lon) =>
        Assert.Contains("latitude", AuctionValidator.Validate(NewPart(lat, lon), requireFutureEnd: true).Keys);

    [Fact]
    public void Missing_location_defaults_to_the_sellers_primary_location()
    {
        var auction = NewPart();
        SellerLocation.ApplyDefault(auction.Item, Seller(
            (SellerLocation.NameClaim, "Lagos"), (SellerLocation.LatitudeClaim, "6.5244"), (SellerLocation.LongitudeClaim, "3.3792")));

        Assert.Equal("Lagos", auction.Item.LocationName);
        Assert.Equal(6.5244, auction.Item.Latitude);
        Assert.Equal(3.3792, auction.Item.Longitude);
    }

    [Fact]
    public void A_location_sent_by_the_seller_is_kept()
    {
        var auction = NewPart(9.0765, 7.3986);
        auction.Item.LocationName = "Abuja";
        SellerLocation.ApplyDefault(auction.Item, Seller(
            (SellerLocation.NameClaim, "Lagos"), (SellerLocation.LatitudeClaim, "6.5244"), (SellerLocation.LongitudeClaim, "3.3792")));

        Assert.Equal("Abuja", auction.Item.LocationName);
        Assert.Equal(9.0765, auction.Item.Latitude);
    }

    [Fact]
    public void No_location_claims_leaves_the_item_without_location()
    {
        var auction = NewPart();
        SellerLocation.ApplyDefault(auction.Item, Seller(("username", "bob")));
        Assert.Null(auction.Item.Latitude);
    }
}
