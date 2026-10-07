namespace AuctionService.Tests;

public class AuctionEntityTests
{
    // Method_Scenario_Expected
    [Fact]
    public void HasReservePrice_WithReserveAboveStartingPrice_ReturnsTrue()
    {
        // arrange
        var auction = TestData.CreateAuction();

        // act
        var result = auction.HasReservePrice();

        // assert
        Assert.True(result);
    }

    [Fact]
    public void HasReservePrice_WithReserveEqualToStartingPrice_ReturnsFalse()
    {
        // arrange
        var auction = TestData.CreateAuction(x => x.ReservePrice = x.StartingPrice);

        // act
        var result = auction.HasReservePrice();

        // assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(99, false)]
    [InlineData(100, true)]
    [InlineData(150, true)]
    public void IsReserveMet_ComparesHighBidWithReserve(int? highBid, bool expected)
    {
        var auction = TestData.CreateAuction(x => x.CurrentHighBid = highBid);

        Assert.Equal(expected, auction.IsReserveMet());
    }
}