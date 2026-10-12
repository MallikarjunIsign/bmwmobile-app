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

    [Theory]
    [InlineData(0, 10)]
    [InlineData(100, 10)]
    [InlineData(999, 10)]
    [InlineData(1_000, 20)]
    [InlineData(4_999, 20)]
    [InlineData(5_000, 50)]
    [InlineData(9_999, 50)]
    [InlineData(10_000, 100)]
    [InlineData(49_999, 100)]
    [InlineData(50_000, 250)]
    [InlineData(2_000_000, 250)]
    public void StepFor_FollowsTheTierTable(int currentTotal, int expectedStep)
    {
        Assert.Equal(expectedStep, BidRules.StepFor(currentTotal));
    }

    [Theory]
    [InlineData(100, 110)]
    [InlineData(1_000, 1_020)]
    [InlineData(5_000, 5_050)]
    [InlineData(10_000, 10_100)]
    [InlineData(50_000, 50_250)]
    public void MinimumBid_IsCurrentTotalPlusStep(int highBid, int expected)
    {
        Assert.Equal(expected, BidRules.MinimumBid(Auction(), HighBid(highBid)));
    }

    [Fact]
    public void MinimumBid_WithoutBids_IsPartPricePlusStep()
    {
        Assert.Equal(5050, BidRules.MinimumBid(Auction(), null));
        Assert.Equal(310, BidRules.MinimumBid(Auction(x => x.StartingPrice = 300), null));
    }

    [Fact]
    public void MinimumBid_IgnoresTheOldPerAuctionIncrement()
    {
        Assert.Equal(5050, BidRules.MinimumBid(Auction(x => x.BidIncrement = 500), HighBid(5000)));
    }

    [Fact]
    public void QuickBids_AreNextPlusOneTwoAndFiveSteps()
    {
        Assert.Equal([1020, 1040, 1060, 1120], BidRules.QuickBids(1020, 20));
    }

    [Fact]
    public void MinimumBidMessage_ContainsTheNextValidAmount()
    {
        Assert.Equal("Your bid is too low. The next valid bid is 5,500", BidRules.MinimumBidMessage(5500));
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