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
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        return await fixture.Host.Scenario(s =>
        {
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

        // next bid pre-filled with the starting price
        var next = await NextMin(auction.Id);
        Assert.Equal(5000, next.MinimumBid);
        Assert.Null(next.CurrentHighBid);

        // 5,000 is accepted below the reserve
        var first = await (await Bid(auction.Id, 5000)).ReadAsJsonAsync<BidDto>();
        Assert.Equal("AcceptedBelowReserve", first.BidStatus);
        Assert.False((await NextMin(auction.Id)).ReserveMet);

        // 5,200 is too low: rejected with the minimum, and not stored
        var tooLow = await Bid(auction.Id, 5200, expected: HttpStatusCode.BadRequest);
        Assert.Contains("Minimum bid is 5,500", await tooLow.ReadAsTextAsync());

        // 10,500 meets the reserve
        var second = await (await Bid(auction.Id, 10500)).ReadAsJsonAsync<BidDto>();
        Assert.Equal("Accepted", second.BidStatus);

        next = await NextMin(auction.Id);
        Assert.True(next.ReserveMet);
        Assert.Equal(10500, next.CurrentHighBid);
        Assert.Equal(11000, next.MinimumBid);

        // only the accepted bids were stored
        var bids = await (await fixture.Host.Scenario(s => s.Get.Url($"/api/bids/{auction.Id}")))
            .ReadAsJsonAsync<List<BidDto>>();
        Assert.Equal(2, bids.Count);

        // the seller accepts the highest bid: Sold to alice
        await Accept(auction.Id, second.Id);
        Assert.Equal(AuctionStatus.Sold, (await Reload(auction.Id)).Status);

        // no more bids after that
        await Bid(auction.Id, 11000, "carol", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PlaceBid_FirstBidBelowStartingPrice_IsRejected()
    {
        var auction = await CreateAuctionAsync();

        var result = await Bid(auction.Id, 4999, expected: HttpStatusCode.BadRequest);

        Assert.Contains("Minimum bid is 5,000", await result.ReadAsTextAsync());
    }

    [Fact]
    public async Task PlaceBid_OnOwnAuction_IsRejected()
    {
        var auction = await CreateAuctionAsync();

        var result = await Bid(auction.Id, 6000, "bob", HttpStatusCode.BadRequest);

        Assert.Contains("own item", await result.ReadAsTextAsync());
    }

    [Fact]
    public async Task PlaceBid_HighestBidderMayRaiseOwnBid()
    {
        var auction = await CreateAuctionAsync();

        await Bid(auction.Id, 5000);
        var raised = await (await Bid(auction.Id, 6000)).ReadAsJsonAsync<BidDto>();

        Assert.Equal(6000, raised.Amount);
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
            .Select(i => Bid(auction.Id, 5000, $"bidder{i}", expected: null)));

        var statuses = results.Select(x => x.Context.Response.StatusCode).ToList();
        Assert.Equal(1, statuses.Count(x => x == 200));
        Assert.Equal(7, statuses.Count(x => x == 400));

        var next = await NextMin(auction.Id);
        Assert.Equal(5000, next.CurrentHighBid);
    }

    [Fact]
    public async Task PlaceBid_ConcurrentIncreasingBids_HighBidIsTheMaximum()
    {
        var auction = await CreateAuctionAsync();

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => Bid(auction.Id, 5000 + i * 500, $"bidder{i}", expected: null)));

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
        var first = await (await Bid(auction.Id, 5000)).ReadAsJsonAsync<BidDto>();
        await Bid(auction.Id, 6000, "carol");

        var result = await Accept(auction.Id, first.Id, expected: HttpStatusCode.Conflict);
        var body = await result.ReadAsTextAsync();

        Assert.Contains("A higher bid just came in", body);
        Assert.Contains("6000", body);
        Assert.Equal(AuctionStatus.Live, (await Reload(auction.Id)).Status);
    }

    [Fact]
    public async Task Accept_ByNonSeller_Returns403()
    {
        var auction = await CreateAuctionAsync();
        var bid = await (await Bid(auction.Id, 5000)).ReadAsJsonAsync<BidDto>();

        await Accept(auction.Id, bid.Id, "alice", HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Accept_BelowReserve_SellsAndPublishesAuctionFinished()
    {
        var auction = await CreateAuctionAsync();
        var bid = await (await Bid(auction.Id, 5000)).ReadAsJsonAsync<BidDto>();

        var tracked = await Track(service => service.AcceptBidAsync(auction.Id, bid.Id, "bob"));

        var finished = tracked.Sent.SingleMessage<AuctionFinished>();
        Assert.True(finished.ItemSold);
        Assert.Equal("alice", finished.Winner);
        Assert.Equal(5000, finished.Amount);
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

        Assert.Equal(7000, (await NextMin(auction.Id)).MinimumBid);
    }

    [Fact]
    public async Task ApplySnapshot_UpdatedPricesAfterFirstBid_AreIgnored()
    {
        var auction = await CreateAuctionAsync();
        await Bid(auction.Id, 5000);

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
            await service.PlaceBidAsync(auction.Id, "alice", 5000);
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

        var tracked = await Track(service => service.PlaceBidAsync(auction.Id, "alice", 5000));

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