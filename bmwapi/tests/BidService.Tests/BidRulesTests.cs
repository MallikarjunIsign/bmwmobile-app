using BidService.Models;
using BidService.Services;

namespace BidService.Tests;

public class BidRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static Auction Auction(Action<Auction>? configure = null)
    {
        var auction = new Auction
        {
            Id = "a1",
            Seller = "bob",
            StartingPrice = 5000,
            ReservePrice = 10000,
            BidIncrement = 500,
            AuctionEnd = Now.AddDays(1)
        };
        configure?.Invoke(auction);
        return auction;
    }

    private static Bid HighBid(int amount) => new() { AuctionId = "a1", Bidder = "alice", Amount = amount };

    [Fact]
    public void MinimumBid_WithoutBids_IsStartingPrice()
    {
        Assert.Equal(5000, BidRules.MinimumBid(Auction(), null));
    }

    [Fact]
    public void MinimumBid_WithHighBid_IsHighBidPlusIncrement()
    {
        Assert.Equal(5500, BidRules.MinimumBid(Auction(), HighBid(5000)));
    }

    [Fact]
    public void MinimumBidMessage_UsesThousandsSeparator()
    {
        Assert.Equal("Minimum bid is 5,500", BidRules.MinimumBidMessage(5500));
    }

    [Theory]
    [InlineData(5000, BidStatus.AcceptedBelowReserve)]
    [InlineData(9999, BidStatus.AcceptedBelowReserve)]
    [InlineData(10000, BidStatus.Accepted)]
    [InlineData(10500, BidStatus.Accepted)]
    public void StatusForAcceptedBid_ComparesWithReserve(int amount, BidStatus expected)
    {
        Assert.Equal(expected, BidRules.StatusForAcceptedBid(Auction(), amount));
    }

    [Fact]
    public void ReserveEqualToStartingPrice_FirstValidBidMeetsReserve()
    {
        var auction = Auction(x => x.ReservePrice = x.StartingPrice);

        Assert.Equal(BidStatus.Accepted, BidRules.StatusForAcceptedBid(auction, auction.StartingPrice));
    }

    [Theory]
    [InlineData(AuctionStatus.Live, 1, true)]
    [InlineData(AuctionStatus.Live, -1, false)]
    [InlineData(AuctionStatus.Sold, 1, false)]
    [InlineData(AuctionStatus.ReserveNotMet, 1, false)]
    public void IsOpenForBids(AuctionStatus status, int endInHours, bool expected)
    {
        var auction = Auction(x =>
        {
            x.Status = status;
            x.AuctionEnd = Now.AddHours(endInHours);
        });

        Assert.Equal(expected, BidRules.IsOpenForBids(auction, Now));
    }

    [Theory]
    [InlineData(false, 60, null)]   // disabled
    [InlineData(true, 600, null)]   // 10 minutes left: outside the 2 minute window
    [InlineData(true, 90, 210)]     // 1.5 minutes left: end moves 2 minutes later
    [InlineData(true, 120, 240)]    // exactly at the window edge
    public void ExtendedEnd_OnlyWithinWindowWhenEnabled(bool enabled, int secondsLeft, int? expectedSecondsLeft)
    {
        var auction = Auction(x => x.AuctionEnd = Now.AddSeconds(secondsLeft));
        var options = new AntiSnipingOptions { Enabled = enabled, WindowMinutes = 2, ExtendMinutes = 2 };

        var extended = BidRules.ExtendedEnd(auction, Now, options);

        Assert.Equal(expectedSecondsLeft is null ? null : Now.AddSeconds(expectedSecondsLeft.Value), extended);
    }

    [Fact]
    public void StatusWhenTimeEnds_WithoutBids_IsUnsold()
    {
        Assert.Equal(AuctionStatus.Unsold, BidRules.StatusWhenTimeEnds(Auction(), null));
    }

    [Fact]
    public void StatusWhenTimeEnds_HighBidBelowReserve_IsReserveNotMet()
    {
        Assert.Equal(AuctionStatus.ReserveNotMet, BidRules.StatusWhenTimeEnds(Auction(), HighBid(9500)));
    }

    [Fact]
    public void StatusWhenTimeEnds_HighBidAtReserve_IsSold()
    {
        Assert.Equal(AuctionStatus.Sold, BidRules.StatusWhenTimeEnds(Auction(), HighBid(10000)));
    }

    [Fact]
    public void CanSellerAccept_WhileLive_IsSellerAccepted()
    {
        var (reason, error) = BidRules.CanSellerAccept(Auction(), Now);

        Assert.Null(error);
        Assert.Equal(EndReason.SellerAccepted, reason);
    }

    [Fact]
    public void CanSellerAccept_DuringGracePeriod_IsSellerAcceptedAfterEnd()
    {
        var auction = Auction(x =>
        {
            x.Status = AuctionStatus.ReserveNotMet;
            x.DecisionDeadline = Now.AddHours(1);
        });

        Assert.Equal(EndReason.SellerAcceptedAfterEnd, BidRules.CanSellerAccept(auction, Now).Reason);
    }

    [Theory]
    [InlineData(AuctionStatus.ReserveNotMet)] // grace period over
    [InlineData(AuctionStatus.Sold)]
    [InlineData(AuctionStatus.Unsold)]
    public void CanSellerAccept_AfterTheEnd_IsRejected(AuctionStatus status)
    {
        var auction = Auction(x =>
        {
            x.Status = status;
            x.DecisionDeadline = Now.AddMinutes(-1);
        });

        Assert.NotNull(BidRules.CanSellerAccept(auction, Now).Error);
    }
}