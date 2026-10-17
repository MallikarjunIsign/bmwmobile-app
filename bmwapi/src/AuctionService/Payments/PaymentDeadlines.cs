using System.Net.Http.Json;
using AuctionService.Data;
using AuctionService.Entities;
using AuctionService.RequestHelpers;
using AuctionService.Services;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine.EntityFrameworkCore;

namespace AuctionService.Payments;

/// <summary>A bid as BidService lists it (GET /api/bids/{auctionId}).</summary>
public record BidRecord(string Bidder, int Amount, string? BidStatus);

/// <summary>The auction's bids (BidService owns them): second-chance offers go to the next-highest bidders.</summary>
public interface IBidHistory
{
    Task<IReadOnlyList<BidRecord>> GetBidsAsync(string auctionId, CancellationToken ct);
}

public class HttpBidHistory(HttpClient http, IOptions<PaymentOptions> options) : IBidHistory
{
    public async Task<IReadOnlyList<BidRecord>> GetBidsAsync(string auctionId, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<BidRecord>>($"{options.Value.BidServiceUrl.TrimEnd('/')}/api/bids/{Uri.EscapeDataString(auctionId)}", ct) ?? [];
}

/// <summary>
/// Non-payment strikes: a winner who doesn't pay in time gets one. Payments:StrikeLimit (2) strikes within
/// Payments:StrikeWindowMonths (12) block bidding (BidService asks over gRPC); an admin can clear them.
/// Declining or not answering a second-chance offer is never a strike.
/// </summary>
public class StrikeService(AuctionDbContext db, IOptions<PaymentOptions> options, TimeProvider clock)
{
    private readonly PaymentOptions _opt = options.Value;

    public async Task<int> ActiveStrikesAsync(string user, CancellationToken ct)
    {
        var since = clock.GetUtcNow().UtcDateTime.AddMonths(-Math.Max(_opt.StrikeWindowMonths, 1));
        return await db.NonPaymentStrikes.CountAsync(s => s.User == user && s.ClearedAt == null && s.CreatedAt > since, ct);
    }

    /// <summary>Why the user may not bid (null = they may).</summary>
    public async Task<string?> BiddingBlockedReasonAsync(string user, CancellationToken ct)
    {
        var strikes = await ActiveStrikesAsync(user, ct);
        return strikes >= Math.Max(_opt.StrikeLimit, 1)
            ? $"You can't bid right now: {strikes} won auctions weren't paid in the last {_opt.StrikeWindowMonths} months. Contact support to have this reviewed."
            : null;
    }

    public async Task<int> ClearAsync(string user, string admin, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var active = await db.NonPaymentStrikes.Where(s => s.User == user && s.ClearedAt == null).ToListAsync(ct);
        foreach (var s in active) (s.ClearedAt, s.ClearedBy) = (now, admin);
        await db.SaveChangesAsync(ct);
        return active.Count;
    }
}

/// <summary>
/// Everything with a deadline, run by <see cref="PaymentJobs"/> every Payments:JobIntervalSeconds:
/// <list type="number">
/// <item>Buy now reservations past their time → released (the payment is cancelled at the provider).</item>
/// <item>Orders waiting for payment → reminders at half time and 2 hours before the deadline.</item>
/// <item>Deadline passed → the order is cancelled "Not paid"; the auction's winner gets a strike; the next-highest bidder
///   gets a second-chance offer at their own highest bid (up to Payments:SecondChanceOffers), else the listing is Unsold.</item>
/// <item>Second-chance offers not answered in time → expired (no strike) → the next bidder.</item>
/// </list>
/// </summary>
public class PaymentDeadlineService(
    AuctionDbContext db,
    IDbContextOutbox<AuctionDbContext> outbox,
    IPaymentProvider provider,
    CheckoutService checkout,
    StrikeService strikes,
    IBidHistory bids,
    AuctionMapper mapper,
    IOptions<PaymentOptions> options,
    TimeProvider clock,
    ILogger<PaymentDeadlineService> logger)
{
    private readonly PaymentOptions _opt = options.Value;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task RunAsync(CancellationToken ct)
    {
        await ReleaseExpiredReservationsAsync(ct);
        await SendRemindersAsync(ct);
        await CloseUnpaidOrdersAsync(ct);
        await ExpireOffersAsync(ct);
    }

    /// <summary>
    /// An auction just sold online: the winner's order waits for payment until the deadline. Returns the notifications
    /// (the caller sends them with its own transaction).
    /// </summary>
    public static IEnumerable<OrderStatusChanged> OpenPaymentWindow(Order order, TimeSpan window, DateTime now)
    {
        order.Status = OrderStatus.AwaitingPayment;
        order.PaymentStatus = OrderPaymentStatus.AwaitingPayment;
        order.CreatedAt = now;
        order.PaymentDueAt = now + window;
        yield return new OrderStatusChanged
        {
            OrderId = order.Id, AuctionId = order.AuctionId, Title = order.Title, NotifyUser = order.Buyer,
            StatusLabel = $"You won! Pay within {Hours(window)} to get it",
        };
        yield return new OrderStatusChanged
        {
            OrderId = order.Id, AuctionId = order.AuctionId, Title = order.Title, NotifyUser = order.Seller,
            StatusLabel = $"Sold – waiting for the buyer's payment ({Hours(window)})",
        };
    }

    private async Task ReleaseExpiredReservationsAsync(CancellationToken ct)
    {
        var now = Now;
        var expired = await db.StockReservations.Where(r => r.Status == ReservationStatus.Active && r.ExpiresAt <= now).ToListAsync(ct);
        foreach (var reservation in expired)
        {
            var auction = await db.Auctions.Include(a => a.Item).FirstAsync(a => a.Id == reservation.AuctionId, ct);
            foreach (var payment in await db.Payments.Where(p => p.ReservationId == reservation.Id &&
                         (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Failed)).ToListAsync(ct))
            {
                if (payment.ProviderPaymentId is { } pi) await provider.CancelPaymentIntentAsync(pi, ct);
                payment.Status = PaymentStatus.Canceled;
                payment.UpdatedAt = now;
            }
            await checkout.ReleaseAsync(reservation, auction, "15 minutes passed", ct);
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
        }
    }

    private async Task SendRemindersAsync(CancellationToken ct)
    {
        var now = Now;
        var waiting = await db.Orders.Where(o => o.Status == OrderStatus.AwaitingPayment && o.PaymentDueAt != null && o.PaymentDueAt > now).ToListAsync(ct);
        foreach (var order in waiting)
        {
            var due = order.PaymentDueAt!.Value;
            var window = due - order.CreatedAt;
            // "2 hours before" – or the last quarter of a shortened (demo) deadline.
            var finalAt = due - TimeSpan.FromTicks(Math.Min(TimeSpan.FromHours(2).Ticks, window.Ticks / 4));
            var halfwayAt = order.CreatedAt + window / 2;
            if (order.FinalReminderAt is null && now >= finalAt)
            {
                order.FinalReminderAt = now;
                order.HalfwayReminderAt ??= now;
                await NotifyAsync(order, order.Buyer, $"Last reminder: pay within {Left(due - now)} or the item goes to the next bidder");
            }
            else if (order.HalfwayReminderAt is null && now >= halfwayAt)
            {
                order.HalfwayReminderAt = now;
                await NotifyAsync(order, order.Buyer, $"Reminder: {Left(due - now)} left to pay");
            }
            else continue;
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
        }
    }

    private async Task CloseUnpaidOrdersAsync(CancellationToken ct)
    {
        var now = Now;
        var overdue = await db.Orders.Where(o => o.Status == OrderStatus.AwaitingPayment && o.PaymentDueAt != null && o.PaymentDueAt <= now).ToListAsync(ct);
        foreach (var order in overdue)
        {
            order.Status = OrderStatus.Cancelled;
            order.PaymentStatus = OrderPaymentStatus.Expired;
            order.CancelledAt = now;
            order.CancelReason = "Not paid in time";
            foreach (var payment in await db.Payments.Where(p => p.OrderId == order.Id && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Failed)).ToListAsync(ct))
            {
                if (payment.ProviderPaymentId is { } pi) await provider.CancelPaymentIntentAsync(pi, ct);
                payment.Status = PaymentStatus.Canceled;
                payment.UpdatedAt = now;
            }

            if (order.SecondChanceRound == 0)
            {
                // The auction's winner didn't pay: a strike.
                db.NonPaymentStrikes.Add(new NonPaymentStrike { User = order.Buyer, AuctionId = order.AuctionId, OrderId = order.Id, CreatedAt = now });
                await NotifyAsync(order, order.Buyer, "Order cancelled: not paid in time. This counts as a non-payment strike");
            }
            else
            {
                if (await db.SecondChanceOffers.FirstOrDefaultAsync(o => o.OrderId == order.Id, ct) is { } offer) offer.Status = SecondChanceStatus.Expired;
                await NotifyAsync(order, order.Buyer, "Offer closed: not paid in time");
            }
            logger.LogInformation("Order {OrderId} not paid in time (round {Round})", order.Id, order.SecondChanceRound);
            await OfferNextAsync(order.AuctionId, ct);
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
        }
    }

    private async Task ExpireOffersAsync(CancellationToken ct)
    {
        var now = Now;
        var expired = await db.SecondChanceOffers.Where(o => o.Status == SecondChanceStatus.Offered && o.ExpiresAt <= now).ToListAsync(ct);
        foreach (var offer in expired)
        {
            // Not answering counts as declining – no strike.
            offer.Status = SecondChanceStatus.Expired;
            offer.AnsweredAt = now;
            await OfferNextAsync(offer.AuctionId, ct);
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
        }
    }

    // ── second-chance offers ─────────────────────────────────────────────

    /// <summary>The bidder accepts: their order waits for payment until the offer's deadline.</summary>
    public async Task<Order> AcceptOfferAsync(string user, string offerId, string? email, string? phone, CancellationToken ct)
    {
        var offer = await db.SecondChanceOffers.FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null || offer.Bidder != user) throw new CheckoutException("Offer not found", "NOT_FOUND", 404);
        if (offer.Status != SecondChanceStatus.Offered || offer.ExpiresAt <= Now)
            throw new CheckoutException("This offer has ended", "OFFER_ENDED", 409);

        var auction = await db.Auctions.WithDetails().FirstAsync(a => a.Id == offer.AuctionId, ct);
        var order = OrderTracking.NewOrder(auction, user, 1, offer.Amount, mapper.CoverUrl(auction));
        order.CreatedAt = Now;
        order.Status = OrderStatus.AwaitingPayment;
        order.PaymentStatus = OrderPaymentStatus.AwaitingPayment;
        order.PaymentDueAt = offer.ExpiresAt;
        order.SecondChanceRound = offer.Round;
        (order.BuyerEmail, order.BuyerPhone) = (email, phone);
        db.Orders.Add(order);

        offer.Status = SecondChanceStatus.Accepted;
        offer.AnsweredAt = Now;
        offer.OrderId = order.Id;
        await outbox.PublishAsync(new OrderCreated { OrderId = order.Id, AuctionId = auction.Id, Seller = auction.Seller, Buyer = user });
        await NotifyAsync(order, auction.Seller, "Next bidder accepted the offer – waiting for their payment");
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
        return order;
    }

    /// <summary>Declining is free (no strike): the offer goes to the next bidder at once.</summary>
    public async Task DeclineOfferAsync(string user, string offerId, CancellationToken ct)
    {
        var offer = await db.SecondChanceOffers.FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null || offer.Bidder != user) throw new CheckoutException("Offer not found", "NOT_FOUND", 404);
        if (offer.Status != SecondChanceStatus.Offered) throw new CheckoutException("This offer has ended", "OFFER_ENDED", 409);
        offer.Status = SecondChanceStatus.Declined;
        offer.AnsweredAt = Now;
        await OfferNextAsync(offer.AuctionId, ct);
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
    }

    /// <summary>
    /// The next-highest bidder (not the seller, not someone already asked, not blocked from bidding, not blocked by the
    /// seller) gets an offer at their own highest bid – up to Payments:SecondChanceOffers; nobody left → Unsold.
    /// The caller saves.
    /// </summary>
    public async Task OfferNextAsync(string auctionId, CancellationToken ct)
    {
        var auction = await db.Auctions.WithDetails().FirstAsync(a => a.Id == auctionId, ct);
        var offers = await db.SecondChanceOffers.Where(o => o.AuctionId == auctionId).ToListAsync(ct);
        // An offer still open, or an accepted one waiting for payment: nothing to do yet.
        var acceptedOrders = offers.Where(o => o.Status == SecondChanceStatus.Accepted && o.OrderId != null).Select(o => o.OrderId!).ToList();
        if (offers.Any(o => o.Status == SecondChanceStatus.Offered) ||
            await db.Orders.AnyAsync(x => acceptedOrders.Contains(x.Id) && x.Status == OrderStatus.AwaitingPayment, ct))
            return;

        var asked = await db.Orders.Where(o => o.AuctionId == auctionId).Select(o => o.Buyer).ToListAsync(ct);
        asked.AddRange(offers.Select(o => o.Bidder));
        string? next = null;
        var amount = 0;
        if (offers.Count < Math.Max(_opt.SecondChanceOffers, 0))
        {
            IReadOnlyList<BidRecord> all;
            try
            {
                all = await bids.GetBidsAsync(auctionId, ct);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                // BidService down: try again on the next run (the caller's other changes are still saved).
                logger.LogWarning("Bid history for {AuctionId} not available: {Message}", auctionId, e.Message);
                return;
            }
            var blockedBySeller = await db.UserBlocks.Where(b => b.Blocker == auction.Seller).Select(b => b.Blocked).ToListAsync(ct);
            foreach (var candidate in all.Where(b => b.BidStatus is null or "Accepted" or "AcceptedBelowReserve")
                         .GroupBy(b => b.Bidder).Select(g => (Bidder: g.Key, Amount: g.Max(b => b.Amount))).OrderByDescending(c => c.Amount))
            {
                if (candidate.Bidder == auction.Seller || asked.Contains(candidate.Bidder) || blockedBySeller.Contains(candidate.Bidder)) continue;
                if (await strikes.BiddingBlockedReasonAsync(candidate.Bidder, ct) is not null) continue;
                (next, amount) = (candidate.Bidder, candidate.Amount);
                break;
            }
        }

        var now = Now;
        if (next is null)
        {
            // Nobody (left) to offer it to: Unsold – the seller can relist.
            auction.Status = Status.Unsold;
            auction.Winner = null;
            auction.SoldAmount = null;
            auction.UpdatedAt = now;
            await outbox.PublishAsync(mapper.ToUpdated(auction));
            await outbox.PublishAsync(new OrderStatusChanged
            {
                OrderId = "", AuctionId = auction.Id, Title = auction.Item.Title ?? auction.Item.DisplayName, NotifyUser = auction.Seller,
                StatusLabel = "Unsold – nobody paid. You can relist it",
            });
            logger.LogInformation("Auction {AuctionId}: nobody paid – Unsold", auction.Id);
            return;
        }

        var offer = new SecondChanceOffer
        {
            AuctionId = auction.Id, Bidder = next, Amount = amount, Round = offers.Count + 1, Status = SecondChanceStatus.Offered,
            OfferedAt = now, ExpiresAt = now + _opt.PaymentWindow,
        };
        db.SecondChanceOffers.Add(offer);
        var title = auction.Item.Title ?? auction.Item.DisplayName;
        await outbox.PublishAsync(new OrderStatusChanged
        {
            OrderId = "", AuctionId = auction.Id, Title = title, NotifyUser = next,
            StatusLabel = $"Second chance: it's yours for your bid of {amount} – accept within {Hours(_opt.PaymentWindow)}",
        });
        await outbox.PublishAsync(new OrderStatusChanged
        {
            OrderId = "", AuctionId = auction.Id, Title = title, NotifyUser = auction.Seller, StatusLabel = "Offered to the next bidder",
        });
        logger.LogInformation("Auction {AuctionId}: second-chance offer {Round} to {Bidder}", auction.Id, offer.Round, next);
    }

    private Task NotifyAsync(Order order, string user, string label) =>
        outbox.PublishAsync(new OrderStatusChanged
        {
            OrderId = order.Id, AuctionId = order.AuctionId, Title = order.Title, NotifyUser = user, StatusLabel = label,
        }).AsTask();

    public static string Hours(TimeSpan span) => span.TotalHours >= 1 ? $"{Math.Round(span.TotalHours)} hours" : $"{Math.Max(1, Math.Round(span.TotalMinutes))} minutes";

    public static string Left(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} min" : $"{Math.Max(1, (int)Math.Ceiling(span.TotalMinutes))} min";
}

/// <summary>Runs <see cref="PaymentDeadlineService"/> every Payments:JobIntervalSeconds (only while payments are on).</summary>
public class PaymentJobs(IServiceScopeFactory scopes, IPaymentProvider provider, IOptions<PaymentOptions> options, ILogger<PaymentJobs> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!provider.IsConfigured) return;
        var interval = TimeSpan.FromSeconds(Math.Clamp(options.Value.JobIntervalSeconds, 5, 3600));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using (var scope = scopes.CreateAsyncScope())
                    await scope.ServiceProvider.GetRequiredService<PaymentDeadlineService>().RunAsync(stoppingToken);
                // Auto-complete and seller payouts (Step 3), in a fresh scope.
                await using (var scope = scopes.CreateAsyncScope())
                    await scope.ServiceProvider.GetRequiredService<SellerPayoutService>().RunAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Payment jobs failed; retrying on the next run");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
