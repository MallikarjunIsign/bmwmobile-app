using BidService.Data;
using BidService.DTOs;
using BidService.Messages;
using BidService.Models;
using Contracts;
using Mapster;
using Microsoft.Extensions.Options;
using Wolverine;

namespace BidService.Services;

public abstract record BidOutcome
{
    public sealed record Ok(BidDto Bid) : BidOutcome;
    public sealed record NotFound(string Message) : BidOutcome;
    public sealed record Invalid(string Message) : BidOutcome;
    public sealed record Forbidden : BidOutcome;
    public sealed record Conflict(string Message, BidDto HighBid) : BidOutcome;
}

/// <summary>
/// Places and accepts bids and ends auctions. Every operation that reads the high bid and then changes state
/// locks the auction row (SELECT ... FOR UPDATE) inside a transaction, so concurrent bids are validated one
/// after the other against the latest high bid. Messages are enlisted in the same transaction (Wolverine outbox),
/// so an event is only sent if the change is committed.
/// </summary>
public class BiddingService(
    IBidRepository repository,
    IMessageContext bus,
    GrpcAuctionClient grpcClient,
    TimeProvider timeProvider,
    IOptions<AntiSnipingOptions> antiSniping,
    IOptions<AuctionRulesOptions> auctionRules,
    ILogger<BiddingService> logger)
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // Lets a check scheduled for exactly AuctionEnd run even if the clocks differ by a moment.
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromSeconds(1);

    /// <summary>The local read model, falling back to AuctionService over gRPC (and caching the result).</summary>
    public async Task<Auction?> GetOrFetchAuctionAsync(string auctionId)
    {
        var auction = await repository.GetAuctionAsync(auctionId);
        if (auction != null) return auction;

        auction = grpcClient.GetAuction(auctionId);
        if (auction == null) return null;

        if (await repository.CreateAuctionAsync(auction))
            await ScheduleChecksAsync(auction);

        return await repository.GetAuctionAsync(auctionId) ?? auction;
    }

    public async Task<BidOutcome> PlaceBidAsync(string auctionId, string bidder, int amount)
    {
        var auction = await GetOrFetchAuctionAsync(auctionId);
        if (auction == null) return new BidOutcome.NotFound("Auction not found");

        if (auction.Seller == bidder) return new BidOutcome.Invalid(BidRules.OwnListingMessage);
        // The seller blocked this user (chat "Block user").
        if (await repository.IsBlockedAsync(auction.Seller, bidder)) return new BidOutcome.Invalid(BidRules.BlockedMessage);

        await using var session = await repository.BeginAsync();
        await bus.EnlistInOutboxAsync(session.Transaction);

        auction = await repository.LockAuctionAsync(session, auctionId) ?? auction;
        var now = Now;

        if (!BidRules.IsOpenForBids(auction, now)) return new BidOutcome.Invalid("Auction has finished");

        var highBid = await repository.GetHighestBidAsync(auctionId, session);
        // A buyer who is already the highest bidder can't outbid themselves.
        if (highBid?.Bidder == bidder) return new BidOutcome.Invalid(BidRules.AlreadyHighestMessage);
        var minimumBid = BidRules.MinimumBid(auction, highBid);

        // Too-low bids are rejected and not stored, so they can never change the high bid.
        if (amount < minimumBid) return new BidOutcome.Invalid(BidRules.MinimumBidMessage(minimumBid));

        var bid = new Bid
        {
            AuctionId = auctionId,
            Bidder = bidder,
            Amount = amount,
            BidTime = now,
            BidStatus = BidRules.StatusForAcceptedBid(auction, amount)
        };
        var bidDto = await repository.InsertBidAsync(session, bid);

        var extendedEnd = BidRules.ExtendedEnd(auction, now, antiSniping.Value);
        if (extendedEnd != null)
        {
            await repository.UpdateAuctionEndAsync(session, auctionId, extendedEnd.Value);
            await ScheduleEndCheckAsync(auctionId, extendedEnd.Value);
            await ScheduleEndingSoonAsync(auctionId, extendedEnd.Value);
            await bus.PublishAsync(new AuctionExtended { AuctionId = auctionId, AuctionEnd = extendedEnd.Value });
            logger.LogInformation("Auction {AuctionId} extended to {AuctionEnd} by a late bid", auctionId, extendedEnd);
        }

        var placed = bidDto.Adapt<BidPlaced>();
        placed.BidCount = await repository.CountAcceptedBidsAsync(auctionId, session);
        placed.Seller = auction.Seller;
        placed.PreviousHighBidder = highBid?.Bidder;
        await bus.PublishAsync(placed);

        await session.CommitAsync();
        await bus.FlushOutgoingMessagesAsync();

        return new BidOutcome.Ok(bidDto);
    }

    /// <summary>The seller accepts the current highest bid (early, or during the ReserveNotMet grace period).</summary>
    public async Task<BidOutcome> AcceptBidAsync(string auctionId, string bidId, string user)
    {
        var auction = await GetOrFetchAuctionAsync(auctionId);
        if (auction == null) return new BidOutcome.NotFound("Auction not found");

        if (auction.Seller != user) return new BidOutcome.Forbidden();

        await using var session = await repository.BeginAsync();
        await bus.EnlistInOutboxAsync(session.Transaction);

        auction = await repository.LockAuctionAsync(session, auctionId) ?? auction;

        var (reason, error) = BidRules.CanSellerAccept(auction, Now);
        if (error != null) return new BidOutcome.Invalid(error);

        var highBid = await repository.GetHighestBidAsync(auctionId, session);
        if (highBid == null) return new BidOutcome.Invalid("There are no bids to accept");

        if (highBid.Id != bidId)
            return new BidOutcome.Conflict("A higher bid just came in", highBid.Adapt<BidDto>());

        if (highBid.Amount < auction.ReservePrice && !auctionRules.Value.AllowAcceptBelowReserve)
            return new BidOutcome.Invalid("The highest bid is below your reserve price");

        await repository.SetStatusAsync(session, auctionId, AuctionStatus.Sold, null);

        await bus.PublishAsync(new AuctionFinished
        {
            ItemSold = true,
            AuctionId = auctionId,
            Seller = auction.Seller,
            Winner = highBid.Bidder,
            Amount = highBid.Amount,
            Status = nameof(AuctionStatus.Sold),
            EndReason = reason.ToString(),
            BidId = highBid.Id,
            Bidders = await repository.GetBiddersAsync(auctionId, session)
        });

        await session.CommitAsync();
        await bus.FlushOutgoingMessagesAsync();

        logger.LogInformation("Seller accepted bid {BidId} ({Amount}) on auction {AuctionId}", highBid.Id, highBid.Amount, auctionId);
        return new BidOutcome.Ok(highBid.Adapt<BidDto>());
    }

    /// <summary>
    /// Time ran out (CheckAuctionEnded): Sold if the high bid meets the reserve, ReserveNotMet (with a grace period
    /// for the seller) if it doesn't, Unsold without bids. Ignored if the auction was extended or already ended.
    /// </summary>
    public async Task EndAuctionAsync(string auctionId)
    {
        await using var session = await repository.BeginAsync();
        await bus.EnlistInOutboxAsync(session.Transaction);

        var auction = await repository.LockAuctionAsync(session, auctionId);
        if (auction == null || auction.Status != AuctionStatus.Live) return;

        var now = Now;
        // Rescheduled (anti-sniping or an edit): a later check will end it.
        if (auction.AuctionEnd > now + ClockTolerance) return;

        var highBid = await repository.GetHighestBidAsync(auctionId, session);
        var status = BidRules.StatusWhenTimeEnds(auction, highBid);
        DateTime? deadline = status == AuctionStatus.ReserveNotMet
            ? now.AddHours(auctionRules.Value.ReserveGracePeriodHours)
            : null;

        await repository.SetStatusAsync(session, auctionId, status, deadline);
        if (deadline != null) await ScheduleDecisionCheckAsync(auctionId, deadline.Value);

        await bus.PublishAsync(new AuctionFinished
        {
            ItemSold = status == AuctionStatus.Sold,
            AuctionId = auctionId,
            Seller = auction.Seller,
            Winner = status == AuctionStatus.Sold ? highBid?.Bidder : null,
            Amount = highBid?.Amount,
            Status = status.ToString(),
            EndReason = nameof(EndReason.TimeEnded),
            SellerDecisionDeadline = deadline,
            BidId = highBid?.Id,
            Bidders = await repository.GetBiddersAsync(auctionId, session)
        });

        await session.CommitAsync();
        await bus.FlushOutgoingMessagesAsync();

        logger.LogInformation("Auction {AuctionId} ended: {Status}", auctionId, status);
    }

    /// <summary>The ReserveNotMet grace period is over and the seller didn't accept: the auction is Unsold.</summary>
    public async Task ExpireSellerDecisionAsync(string auctionId)
    {
        await using var session = await repository.BeginAsync();
        await bus.EnlistInOutboxAsync(session.Transaction);

        var auction = await repository.LockAuctionAsync(session, auctionId);
        if (auction == null || auction.Status != AuctionStatus.ReserveNotMet) return;
        if (auction.DecisionDeadline > Now + ClockTolerance) return;

        var highBid = await repository.GetHighestBidAsync(auctionId, session);
        await repository.SetStatusAsync(session, auctionId, AuctionStatus.Unsold, null);

        await bus.PublishAsync(new AuctionFinished
        {
            ItemSold = false,
            AuctionId = auctionId,
            Seller = auction.Seller,
            Amount = highBid?.Amount,
            Status = nameof(AuctionStatus.Unsold),
            EndReason = nameof(EndReason.GracePeriodExpired),
            BidId = highBid?.Id,
            Bidders = await repository.GetBiddersAsync(auctionId, session)
        });

        await session.CommitAsync();
        await bus.FlushOutgoingMessagesAsync();

        logger.LogInformation("Auction {AuctionId} is unsold: the seller didn't accept within the grace period", auctionId);
    }

    /// <summary>
    /// Applies AuctionCreated / AuctionUpdated. Pricing and end time only change while the auction is Live and
    /// has no accepted bids (the same rule AuctionService enforces, re-checked here against the real bids).
    /// </summary>
    public async Task ApplySnapshotAsync(AuctionSnapshot snapshot)
    {
        // Fixed-price listings are bought in AuctionService, never bid on. One switched from an auction (only possible
        // before the first bid) leaves bidding.
        if (snapshot.SaleType == "FixedPrice")
        {
            if (await repository.GetAuctionAsync(snapshot.Id) is { } tracked && tracked.Status == AuctionStatus.Live)
            {
                await using var check = await repository.BeginAsync();
                var hasBids = await repository.HasAcceptedBidsAsync(check, snapshot.Id);
                await check.CommitAsync();
                if (hasBids) logger.LogWarning("Auction {AuctionId} has bids; ignoring the switch to fixed price", snapshot.Id);
                else await repository.DeleteAuctionAsync(snapshot.Id);
            }
            return;
        }

        var incoming = FromSnapshot(snapshot);

        if (await repository.GetAuctionAsync(snapshot.Id) == null)
        {
            if (await repository.CreateAuctionAsync(incoming))
                await ScheduleChecksAsync(incoming);
            return;
        }

        if (snapshot is AuctionCreated) return; // duplicate delivery

        await using var session = await repository.BeginAsync();
        await bus.EnlistInOutboxAsync(session.Transaction);

        var current = await repository.LockAuctionAsync(session, snapshot.Id);
        if (current == null || current.Status != AuctionStatus.Live) return;

        if (await repository.HasAcceptedBidsAsync(session, snapshot.Id))
        {
            logger.LogWarning("Ignoring pricing/end changes for auction {AuctionId}: it already has bids", snapshot.Id);
            return;
        }

        await repository.UpdateAuctionTermsAsync(session, incoming);
        if (incoming.AuctionEnd != current.AuctionEnd)
        {
            await ScheduleEndCheckAsync(incoming.Id, incoming.AuctionEnd);
            await ScheduleEndingSoonAsync(incoming.Id, incoming.AuctionEnd);
        }

        await session.CommitAsync();
        await bus.FlushOutgoingMessagesAsync();
    }

    public static Auction FromSnapshot(AuctionSnapshot snapshot)
    {
        var status = Enum.TryParse<AuctionStatus>(snapshot.Status, out var parsed) ? parsed : AuctionStatus.Live;

        return new Auction
        {
            Id = snapshot.Id,
            AuctionEnd = DateTime.SpecifyKind(snapshot.AuctionEnd, DateTimeKind.Utc),
            Seller = snapshot.Seller,
            StartingPrice = Math.Max(snapshot.StartingPrice, 1),
            // No reserve price in this phase: a bid at the end always sells (reserve = part price).
            ReservePrice = Math.Max(snapshot.StartingPrice, 1),
            BidIncrement = Math.Max(snapshot.BidIncrement, 1),
            Status = status,
            DecisionDeadline = status == AuctionStatus.ReserveNotMet ? snapshot.SellerDecisionDeadline : null,
            Finished = status != AuctionStatus.Live
        };
    }

    private async Task ScheduleChecksAsync(Auction auction)
    {
        if (auction.Status == AuctionStatus.Live)
        {
            await ScheduleEndCheckAsync(auction.Id, auction.AuctionEnd);
            await ScheduleEndingSoonAsync(auction.Id, auction.AuctionEnd);
        }
        else if (auction.Status == AuctionStatus.ReserveNotMet && auction.DecisionDeadline != null)
            await ScheduleDecisionCheckAsync(auction.Id, auction.DecisionDeadline.Value);
    }

    private ValueTask ScheduleEndCheckAsync(string auctionId, DateTime auctionEnd) =>
        bus.ScheduleAsync(new CheckAuctionEnded(auctionId), AsUtc(auctionEnd));

    /// <summary>"Ending in 1 hour" – only when that moment is still ahead.</summary>
    private async ValueTask ScheduleEndingSoonAsync(string auctionId, DateTime auctionEnd)
    {
        var at = auctionEnd - EndingSoonBefore;
        if (at > Now) await bus.ScheduleAsync(new CheckAuctionEndingSoon(auctionId, auctionEnd), AsUtc(at));
    }

    public static readonly TimeSpan EndingSoonBefore = TimeSpan.FromHours(1);

    /// <summary>
    /// One hour before the end: tells the seller and every bidder (AuctionEndingSoon). Ignored when the auction ended
    /// or its end moved (a newer check was scheduled for the new end).
    /// </summary>
    public async Task NotifyEndingSoonAsync(string auctionId, DateTime scheduledFor)
    {
        var auction = await repository.GetAuctionAsync(auctionId);
        if (auction == null || auction.Status != AuctionStatus.Live) return;
        if (Math.Abs((auction.AuctionEnd - scheduledFor).TotalSeconds) > 1) return;

        var highBid = await repository.GetHighestBidAsync(auctionId);
        await bus.PublishAsync(new AuctionEndingSoon
        {
            AuctionId = auctionId,
            Seller = auction.Seller,
            AuctionEnd = auction.AuctionEnd,
            Bidders = await repository.GetBiddersAsync(auctionId),
            HighBidder = highBid?.Bidder,
            CurrentHighBid = highBid?.Amount
        });
    }

    private ValueTask ScheduleDecisionCheckAsync(string auctionId, DateTime deadline) =>
        bus.ScheduleAsync(new CheckSellerDecision(auctionId), AsUtc(deadline));

    private static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}