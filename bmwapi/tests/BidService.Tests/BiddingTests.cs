using System.Net;
using System.Security.Claims;
using Alba;
using BidService.Data;
using BidService.DTOs;
using BidService.Models;
using BidService.Services;
using Contracts;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.Tracking;

namespace BidService.Tests;

[Collection("bid-service")]
public class BiddingTests(AppFixture fixture)
{
    private DateTime Now => fixture.Clock.GetUtcNow().UtcDateTime;

    /// <summary>bob's auction: start 5,000 / reserve 10,000 / increment 500 (the demo numbers).</summary>
    private async Task<Auction> CreateAuctionAsync(Action<Auction>? configure = null)
    {
        var auction = new Auction
        {
            Id = Guid.NewGuid().ToString(),
            Seller = "bob",
            StartingPrice = 5000,
            ReservePrice = 10000,
            BidIncrement = 500,
            AuctionEnd = Now.AddHours(1)
        };
        configure?.Invoke(auction);

        using var scope = fixture.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IBidRepository>().CreateAuctionAsync(auction);
        return auction;
    }

    private async Task<IScenarioResult> Bid(string auctionId, int amount, string bidder = "alice",
        HttpStatusCode? expected = HttpStatusCode.OK)
    {
        return await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", bidder));
            s.Post.Json(new { }).ToUrl("/api/bids")
                .QueryString("auctionId", auctionId)
                .QueryString("amount", amount.ToString());
            if (expected is null) s.IgnoreStatusCode();
            else s.StatusCodeShouldBe(expected.Value);
        });
    }

    private async Task<IScenarioResult> Accept(string auctionId, string bidId, string user = "bob",
        HttpStatusCode expected = HttpStatusCode.OK, string role = "seller")
    {
        return await fixture.Host.Scenario(s =>
        {
            s.RemoveClaim("role");
            s.WithClaim(new Claim("role", role));
            s.WithClaim(new Claim("username", user));
            s.Post.Json(new AcceptBidDto { AuctionId = auctionId, BidId = bidId }).ToUrl("/api/bids/accept");
            s.StatusCodeShouldBe(expected);
        });
    }

    private async Task<NextMinimumBidDto> NextMin(string auctionId)
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.RemoveRequestHeader("Authorization");
            s.Get.Url($"/api/bids/{auctionId}/next-min");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        return await result.ReadAsJsonAsync<NextMinimumBidDto>();
    }

    private async Task<Auction> Reload(string auctionId)
    {
        using var scope = fixture.Host.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IBidRepository>().GetAuctionAsync(auctionId))!;
    }

    /// <summary>Runs a BiddingService operation and returns the messages it sent.</summary>
    private async Task<ITrackedSession> Track(Func<BiddingService, Task> action)
    {
        return await fixture.Host.ExecuteAndWaitAsync(async (IMessageContext context) =>
        {
            using var scope = fixture.Host.Services.CreateScope();
            var service = ActivatorUtilities.CreateInstance<BiddingService>(scope.ServiceProvider, context);
            await action(service);
        });
    }

    [Fact]
    public async Task DemoScenario_MinimumBidReserveAndSellerAccept()
    {
        var auction = await CreateAuctionAsync();

        // next bid pre-filled with part price + step: 5,000 + 50 = 5,050
        var next = await NextMin(auction.Id);
        Assert.Equal(5050, next.MinimumBid);
        Assert.Equal(5000, next.CurrentTotal);
        Assert.Equal(50, next.BidStep);
        Assert.Equal(5000, next.PartPrice);
        Assert.Equal([5050, 5100, 5150, 5300], next.QuickBids);
        Assert.Equal(0, next.BidCount);
        Assert.Null(next.CurrentHighBid);

        // 5,050 is accepted below the reserve
        var first = await (await Bid(auction.Id, 5050)).ReadAsJsonAsync<BidDto>();
        Assert.Equal("AcceptedBelowReserve", first.BidStatus);
        Assert.False((await NextMin(auction.Id)).ReserveMet);

        // 5,080 is too low (5,050 + 50 = 5,100): rejected with the next valid amount, and not stored
        var tooLow = await Bid(auction.Id, 5080, "carol", expected: HttpStatusCode.BadRequest);
        Assert.Contains("next valid bid is 5,100", await tooLow.ReadAsTextAsync());

        // 10,500 meets the reserve
        var second = await (await Bid(auction.Id, 10500, "carol")).ReadAsJsonAsync<BidDto>();
        Assert.Equal("Accepted", second.BidStatus);

        next = await NextMin(auction.Id);
        Assert.True(next.ReserveMet);
        Assert.Equal(10500, next.CurrentHighBid);
        Assert.Equal(10500, next.CurrentTotal);
        Assert.Equal(100, next.BidStep);
        Assert.Equal(10600, next.MinimumBid);
        Assert.Equal(2, next.BidCount);
        Assert.Equal("carol", next.HighBidder);

        // only the accepted bids were stored
        var bids = await (await fixture.Host.Scenario(s => s.Get.Url($"/api/bids/{auction.Id}")))
            .ReadAsJsonAsync<List<BidDto>>();
        Assert.Equal(2, bids.Count);

        // the seller accepts the highest bid: Sold to carol
        await Accept(auction.Id, second.Id);
        Assert.Equal(AuctionStatus.Sold, (await Reload(auction.Id)).Status);

        // no more bids after that
        await Bid(auction.Id, 11000, "dave", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PlaceBid_FirstBidBelowStartingPrice_IsRejected()
    {
        var auction = await CreateAuctionAsync();

        // Equal to the part price is not enough: a bid adds at least one step on top of it.
        var result = await Bid(auction.Id, 5000, expected: HttpStatusCode.BadRequest);

        Assert.Contains("next valid bid is 5,050", await result.ReadAsTextAsync());
    }

    [Fact]
    public async Task NextMinimum_NewRouteMatchesTheOldOne()
    {
        var auction = await CreateAuctionAsync(x => x.StartingPrice = 1000);

        var result = await fixture.Host.Scenario(s =>
        {
            s.RemoveRequestHeader("Authorization");
            s.Get.Url($"/api/bids/{auction.Id}/next-minimum");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        var next = await result.ReadAsJsonAsync<NextMinimumBidDto>();

        Assert.Equal(1000, next.CurrentTotal);
        Assert.Equal(20, next.BidStep);
        Assert.Equal(1020, next.MinimumBid);
    }

    [Fact]
    public async Task Steps_ReturnsTheTierTable()
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.RemoveRequestHeader("Authorization");
            s.Get.Url("/api/bids/steps");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        var body = await result.ReadAsTextAsync();

        Assert.Contains("\"from\":0,\"to\":999,\"step\":10", body);
        Assert.Contains("\"from\":50000,\"to\":null,\"step\":250", body);
    }

    [Fact]
    public async Task PlaceBid_OnOwnAuction_IsRejected()
    {
        var auction = await CreateAuctionAsync();

        var result = await Bid(auction.Id, 6000, "bob", HttpStatusCode.BadRequest);

        Assert.Contains("You can't bid on your own listing", await result.ReadAsTextAsync());
    }

    [Fact]
    public async Task PlaceBid_BlockedBySellerInChat_IsRejected_UntilUnblocked()
    {
        var auction = await CreateAuctionAsync();

        await fixture.Host.InvokeMessageAndWaitAsync(new UserBlocked { Blocker = "bob", Blocked = "mallory" });
        var blocked = await Bid(auction.Id, 6000, "mallory", HttpStatusCode.BadRequest);
        Assert.Contains("You can't bid on this seller's listings", await blocked.ReadAsTextAsync());
        await Bid(auction.Id, 6000, "alice"); // others still can

        await fixture.Host.InvokeMessageAndWaitAsync(new UserUnblocked { Blocker = "bob", Blocked = "mallory" });
        await Bid(auction.Id, 7000, "mallory");
    }

    [Fact]
    public async Task PlaceBid_HighestBidderCannotOutbidThemselves()
    {
        var auction = await CreateAuctionAsync();

        await Bid(auction.Id, 5050);
        var result = await Bid(auction.Id, 6000, expected: HttpStatusCode.BadRequest);

        Assert.Contains("You're the highest bidder", await result.ReadAsTextAsync());
        // Someone else can still outbid.
        await Bid(auction.Id, 6000, "carol");
    }

    [Fact]
    public async Task BidPlaced_CarriesSellerPreviousHighBidderAndCount()
    {
        var auction = await CreateAuctionAsync();
        await Bid(auction.Id, 5050);

        var tracked = await Track(service => service.PlaceBidAsync(auction.Id, "carol", 5100));

        var placed = tracked.Sent.SingleMessage<BidPlaced>();
        Assert.Equal("bob", placed.Seller);
        Assert.Equal("alice", placed.PreviousHighBidder);
        Assert.Equal(2, placed.BidCount);
    }

    [Fact]
    public async Task MyBids_ShowsWinningAndOutbid()
    {
        var first = await CreateAuctionAsync();
        var second = await CreateAuctionAsync();
        await Bid(first.Id, 5050);
        await Bid(second.Id, 5050);
        await Bid(second.Id, 5100, "carol");

        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "alice"));
            s.Get.Url("/api/bids/mine");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        var mine = await result.ReadAsJsonAsync<List<MyBidDto>>();

        Assert.Equal("Winning", mine.Single(x => x.AuctionId == first.Id).State);
        var outbid = mine.Single(x => x.AuctionId == second.Id);
        Assert.Equal("Outbid", outbid.State);
        Assert.Equal(5050, outbid.MyHighestBid);
        Assert.Equal(5100, outbid.CurrentHighBid);
        Assert.Equal(2, outbid.BidCount);
    }

    [Fact]
    public async Task EndAuction_LiveWithBids_ListsTheBidders()
    {
        var auction = await CreateAuctionAsync(x => { x.AuctionEnd = Now.AddSeconds(30); x.ReservePrice = x.StartingPrice; });
        await Bid(auction.Id, 5050);
        await Bid(auction.Id, 5100, "carol");
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        var tracked = await Track(service => service.EndAuctionAsync(auction.Id));

        var finished = tracked.Sent.SingleMessage<AuctionFinished>();
        Assert.Equal("Sold", finished.Status);
        Assert.Equal("carol", finished.Winner);
        Assert.Equal(["alice", "carol"], finished.Bidders.Order());
    }

    [Fact]
    public async Task EndingSoon_NotifiesSellerAndBidders_OnlyForTheCurrentEnd()
    {
        var auction = await CreateAuctionAsync(x => x.AuctionEnd = Now.AddMinutes(30));
        await Bid(auction.Id, 5050);

        var tracked = await Track(service => service.NotifyEndingSoonAsync(auction.Id, auction.AuctionEnd));
        var soon = tracked.Sent.SingleMessage<AuctionEndingSoon>();
        Assert.Equal("bob", soon.Seller);
        Assert.Equal(["alice"], soon.Bidders);

        // A check scheduled for an older end time does nothing.
        var stale = await Track(service => service.NotifyEndingSoonAsync(auction.Id, auction.AuctionEnd.AddHours(-2)));
        Assert.Empty(stale.Sent.MessagesOf<AuctionEndingSoon>());
    }

    [Fact]
    public void FromSnapshot_IgnoresTheReservePrice()
    {
        var auction = BiddingService.FromSnapshot(Snapshot("x", startingPrice: 300));

        Assert.Equal(300, auction.ReservePrice);
    }

    [Fact]
    public async Task PlaceBid_AfterEndTime_IsRejected()
    {
        var auction = await CreateAuctionAsync(x => x.AuctionEnd = Now.AddMinutes(-1));

        var result = await Bid(auction.Id, 6000, expected: HttpStatusCode.BadRequest);

        Assert.Contains("finished", await result.ReadAsTextAsync());
    }

    [Fact]
    public async Task PlaceBid_UnknownAuction_Returns404()
    {
        await Bid("does-not-exist", 6000, expected: HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PlaceBid_ConcurrentBidsOfTheSameAmount_OnlyOneIsAccepted()
    {
        var auction = await CreateAuctionAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => Bid(auction.Id, 5050, $"bidder{i}", expected: null)));

        var statuses = results.Select(x => x.Context.Response.StatusCode).ToList();
        Assert.Equal(1, statuses.Count(x => x == 200));
        Assert.Equal(7, statuses.Count(x => x == 400));

        var next = await NextMin(auction.Id);
        Assert.Equal(5050, next.CurrentHighBid);
    }

    [Fact]
    public async Task PlaceBid_ConcurrentIncreasingBids_HighBidIsTheMaximum()
    {
        var auction = await CreateAuctionAsync();

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => Bid(auction.Id, 5050 + i * 500, $"bidder{i}", expected: null)));

        // whatever order they were processed in, every stored bid respected the minimum at that moment
        var bids = await (await fixture.Host.Scenario(s => s.Get.Url($"/api/bids/{auction.Id}")))
            .ReadAsJsonAsync<List<BidDto>>();
        var inOrder = bids.OrderBy(x => x.BidTime).ThenBy(x => x.Amount).Select(x => x.Amount).ToList();
        for (var i = 1; i < inOrder.Count; i++)
            Assert.True(inOrder[i] >= inOrder[i - 1] + 500);
        Assert.Equal(inOrder.Max(), (await NextMin(auction.Id)).CurrentHighBid);
    }

    [Fact]
    public async Task Accept_WhenOutbid_Returns409WithNewHighBid()
    {
        var auction = await CreateAuctionAsync();
        var first = await (await Bid(auction.Id, 5050)).ReadAsJsonAsync<BidDto>();
        await Bid(auction.Id, 6000, "carol");

        var result = await Accept(auction.Id, first.Id, expected: HttpStatusCode.Conflict);
        var body = await result.ReadAsTextAsync();

        Assert.Contains("A higher bid just came in", body);
        Assert.Contains("6000", body);
        Assert.Equal(AuctionStatus.Live, (await Reload(auction.Id)).Status);
    }

    [Fact]
    public async Task Accept_ByBuyerAccount_Returns403WithFriendlyMessage()
    {
        var auction = await CreateAuctionAsync();
        var bid = await (await Bid(auction.Id, 5050, "carol")).ReadAsJsonAsync<BidDto>();

        // Even on their own auction, an account without the seller role can't accept bids.
        var result = await Accept(auction.Id, bid.Id, "bob", HttpStatusCode.Forbidden, role: "buyer");
        var body = await result.ReadAsTextAsync();
        Assert.Contains("SELLER_ONLY", body);
        Assert.Contains("Only seller accounts", body);
    }

    [Fact]
    public async Task Accept_ByNonSeller_Returns403()
    {
        var auction = await CreateAuctionAsync();
        var bid = await (await Bid(auction.Id, 5050)).ReadAsJsonAsync<BidDto>();

        await Accept(auction.Id, bid.Id, "alice", HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Accept_BelowReserve_SellsAndPublishesAuctionFinished()
    {
        var auction = await CreateAuctionAsync();
        var bid = await (await Bid(auction.Id, 5050)).ReadAsJsonAsync<BidDto>();

        var tracked = await Track(service => service.AcceptBidAsync(auction.Id, bid.Id, "bob"));

        var finished = tracked.Sent.SingleMessage<AuctionFinished>();
        Assert.True(finished.ItemSold);
        Assert.Equal("alice", finished.Winner);
        Assert.Equal(5050, finished.Amount);
        Assert.Equal("Sold", finished.Status);
        Assert.Equal("SellerAccepted", finished.EndReason);
    }

    [Fact]
    public async Task EndAuction_HighBidMeetsReserve_IsSold()
    {
        var auction = await CreateAuctionAsync(x => x.AuctionEnd = Now.AddSeconds(30));
        await Bid(auction.Id, 10000);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        var tracked = await Track(service => service.EndAuctionAsync(auction.Id));

        var finished = tracked.Sent.SingleMessage<AuctionFinished>();
        Assert.Equal("Sold", finished.Status);
        Assert.Equal("TimeEnded", finished.EndReason);
        Assert.Equal("alice", finished.Winner);
        Assert.Equal(AuctionStatus.Sold, (await Reload(auction.Id)).Status);
    }

    [Fact]
    public async Task EndAuction_WithoutBids_IsUnsold()
    {
        var auction = await CreateAuctionAsync(x => x.AuctionEnd = Now.AddSeconds(30));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        var tracked = await Track(service => service.EndAuctionAsync(auction.Id));

        Assert.Equal("Unsold", tracked.Sent.SingleMessage<AuctionFinished>().Status);
        Assert.Equal(AuctionStatus.Unsold, (await Reload(auction.Id)).Status);
    }

    [Fact]
    public async Task EndAuction_BeforeEndTime_DoesNothing()
    {
        // e.g. the check scheduled for the original end of an auction that has since been extended
        var auction = await CreateAuctionAsync(x => x.AuctionEnd = Now.AddMinutes(10));

        var tracked = await Track(service => service.EndAuctionAsync(auction.Id));

        Assert.Empty(tracked.Sent.MessagesOf<AuctionFinished>());
        Assert.Equal(AuctionStatus.Live, (await Reload(auction.Id)).Status);
    }

    [Fact]
    public async Task EndAuction_BelowReserve_GracePeriodThenSellerAccepts()
    {
        var auction = await CreateAuctionAsync(x => x.AuctionEnd = Now.AddSeconds(30));
        var bid = await (await Bid(auction.Id, 6000)).ReadAsJsonAsync<BidDto>();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        var tracked = await Track(service => service.EndAuctionAsync(auction.Id));

        var reserveNotMet = tracked.Sent.SingleMessage<AuctionFinished>();
        Assert.Equal("ReserveNotMet", reserveNotMet.Status);
        Assert.False(reserveNotMet.ItemSold);
        Assert.Equal(6000, reserveNotMet.Amount);
        Assert.Equal(Now.AddHours(24), reserveNotMet.SellerDecisionDeadline!.Value, TimeSpan.FromSeconds(5));

        // no new bids while waiting for the seller
        await Bid(auction.Id, 7000, "carol", HttpStatusCode.BadRequest);

        // the seller accepts within the grace period
        fixture.Clock.Advance(TimeSpan.FromHours(2));
        var accepted = await Track(service => service.AcceptBidAsync(auction.Id, bid.Id, "bob"));

        var sold = accepted.Sent.SingleMessage<AuctionFinished>();
        Assert.Equal("Sold", sold.Status);
        Assert.Equal("SellerAcceptedAfterEnd", sold.EndReason);
        Assert.Equal("alice", sold.Winner);
    }

    [Fact]
    public async Task EndAuction_BelowReserve_GracePeriodExpires_IsUnsold()
    {
        var auction = await CreateAuctionAsync(x => x.AuctionEnd = Now.AddSeconds(30));
        var bid = await (await Bid(auction.Id, 6000)).ReadAsJsonAsync<BidDto>();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await Track(service => service.EndAuctionAsync(auction.Id));

        fixture.Clock.Advance(TimeSpan.FromHours(25));
        var tracked = await Track(service => service.ExpireSellerDecisionAsync(auction.Id));

        var unsold = tracked.Sent.SingleMessage<AuctionFinished>();
        Assert.Equal("Unsold", unsold.Status);
        Assert.Equal("GracePeriodExpired", unsold.EndReason);
        Assert.Equal(AuctionStatus.Unsold, (await Reload(auction.Id)).Status);

        // too late to accept now
        await Accept(auction.Id, bid.Id, expected: HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExpireSellerDecision_AfterSellerAccepted_DoesNothing()
    {
        var auction = await CreateAuctionAsync(x => x.AuctionEnd = Now.AddSeconds(30));
        var bid = await (await Bid(auction.Id, 6000)).ReadAsJsonAsync<BidDto>();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await Track(service => service.EndAuctionAsync(auction.Id));
        await Accept(auction.Id, bid.Id);

        fixture.Clock.Advance(TimeSpan.FromHours(25));
        var tracked = await Track(service => service.ExpireSellerDecisionAsync(auction.Id));

        Assert.Empty(tracked.Sent.MessagesOf<AuctionFinished>());
        Assert.Equal(AuctionStatus.Sold, (await Reload(auction.Id)).Status);
    }

    [Fact]
    public async Task ApplySnapshot_UpdatedPricesBeforeFirstBid_AreUsed()
    {
        var auction = await CreateAuctionAsync();

        await Track(service => service.ApplySnapshotAsync(Snapshot(auction.Id, startingPrice: 7000)));

        Assert.Equal(7050, (await NextMin(auction.Id)).MinimumBid);
    }

    [Fact]
    public async Task ApplySnapshot_UpdatedPricesAfterFirstBid_AreIgnored()
    {
        var auction = await CreateAuctionAsync();
        await Bid(auction.Id, 5050);

        await Track(service => service.ApplySnapshotAsync(Snapshot(auction.Id, startingPrice: 1)));

        var reloaded = await Reload(auction.Id);
        Assert.Equal(5000, reloaded.StartingPrice);
        Assert.Equal(500, reloaded.BidIncrement);
    }

    [Fact]
    public async Task AntiSniping_WhenEnabled_LateBidExtendsTheAuction()
    {
        var auction = await CreateAuctionAsync(x => x.AuctionEnd = Now.AddSeconds(60));
        var enabled = Microsoft.Extensions.Options.Options.Create(
            new AntiSnipingOptions { Enabled = true, WindowMinutes = 2, ExtendMinutes = 2 });

        var tracked = await fixture.Host.ExecuteAndWaitAsync(async (IMessageContext context) =>
        {
            using var scope = fixture.Host.Services.CreateScope();
            var service = ActivatorUtilities.CreateInstance<BiddingService>(scope.ServiceProvider, context, enabled);
            await service.PlaceBidAsync(auction.Id, "alice", 5050);
        });

        var extended = tracked.Sent.SingleMessage<AuctionExtended>();
        Assert.Equal(auction.AuctionEnd.AddMinutes(2), extended.AuctionEnd, TimeSpan.FromMilliseconds(1));
        Assert.Equal(extended.AuctionEnd, (await Reload(auction.Id)).AuctionEnd, TimeSpan.FromMilliseconds(1));

        // the check scheduled for the original end time no longer ends it
        fixture.Clock.Advance(TimeSpan.FromSeconds(90));
        var check = await Track(service => service.EndAuctionAsync(auction.Id));
        Assert.Empty(check.Sent.MessagesOf<AuctionFinished>());
        Assert.Equal(AuctionStatus.Live, (await Reload(auction.Id)).Status);
    }

    [Fact]
    public async Task AntiSniping_DisabledByDefault_LateBidDoesNotExtend()
    {
        var auction = await CreateAuctionAsync(x => x.AuctionEnd = Now.AddSeconds(60));

        var tracked = await Track(service => service.PlaceBidAsync(auction.Id, "alice", 5050));

        Assert.Empty(tracked.Sent.MessagesOf<AuctionExtended>());
        Assert.Equal(auction.AuctionEnd, (await Reload(auction.Id)).AuctionEnd, TimeSpan.FromMilliseconds(1));
    }

    private AuctionUpdated Snapshot(string id, int startingPrice) => new()
    {
        Id = id,
        Seller = "bob",
        Status = "Live",
        ItemType = "Part",
        Make = "Toyota",
        Model = "Hilux",
        Description = "Front bumper",
        StartingPrice = startingPrice,
        ReservePrice = 10000,
        BidIncrement = 500,
        AuctionEnd = Now.AddHours(1)
    };
}
