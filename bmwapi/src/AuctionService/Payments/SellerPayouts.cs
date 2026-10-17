using AuctionService.Data;
using AuctionService.Entities;
using AuctionService.Services;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine.EntityFrameworkCore;

namespace AuctionService.Payments;

/// <summary>The seller's payout as the app shows it on the order (cents of the listing's currency).</summary>
public record PayoutView(long Amount, string Currency, bool Approximate, string Status, string Label, DateTime? TransferredAt);

/// <summary>What the order card needs about the online payment (null for orders without one).</summary>
public record OrderPaymentInfo(string? ReceiptUrl, long RefundedAmount, PayoutView? Payout);

public record RefundLineDto(long Amount, string Reason, string Status, DateTime CreatedAt);

/// <summary>
/// The order's receipt. Buyer: what they paid (item, shipping, total, card, refunds, Stripe's receipt page). Seller: the
/// payout statement (item + shipping − the app's commission = their share, and where the transfer is).
/// </summary>
public record ReceiptDto(
    string OrderId, string Role, string Title, int Quantity, string Currency,
    long ItemAmount, long ShippingAmount, long TotalAmount, DateTime? PaidAt, string? Card, string? ReceiptUrl,
    long RefundedAmount, List<RefundLineDto> Refunds,
    decimal CommissionPercent, long? CommissionAmount, PayoutView? Payout, string? DisputeStatus);

/// <summary>A payment as the admin list shows it.</summary>
public record AdminPaymentDto(
    string PaymentId, string? OrderId, string AuctionId, string Buyer, string Seller, string Currency, long TotalAmount,
    long CommissionAmount, long RefundedAmount, string Status, string? DisputeStatus, DateTime CreatedAt, DateTime? PaidAt,
    string? OrderStatus, DateTime? OnHoldSince, string? HoldReason, DateTime? AutoCompleteAt,
    string? TransferId, string? TransferStatus, long? TransferAmount, string? TransferCurrency, long? SellerAmount,
    string? SellerCurrency, long? ArrivedAmount, string? ArrivedCurrency, string? TransferFailure);

public record AdminOrderPaymentDto(AdminPaymentDto Payment, List<RefundLineDto> Refunds, string? ReceiptUrl, string? ProviderPaymentId,
    string? ProviderChargeId, string? ProviderTransferId, string? ProviderDisputeId, decimal? ExchangeRate);

/// <summary>
/// Step 3: paying the seller and everything that can change it (design: docs/PAYMENTS_DESIGN.md §9).
/// <list type="bullet">
/// <item>Completed (the buyer confirms, or automatically Payments:AutoCompleteDays after Picked up / Shipped) → the seller's
///   share (item + shipping − commission − refunds) is transferred from the buyer's charge to their account. The platform
///   balance is in USD: a EUR payment's share is converted at the charge's rate and the Irish seller receives about that in
///   EUR. Not sent yet (account not Active, provider error) → Pending, retried by the job.</item>
/// <item>On hold (the buyer reported a problem, a dispute, an admin) → no auto-complete and no transfer until released.</item>
/// <item>Refunds (admin: any amount; seller: everything, before the handover) go back to the card; after a payout the
///   seller's part is taken back (transfer reversal). Refunds made in Stripe's Dashboard are picked up from the webhook.</item>
/// <item>Disputes freeze the payout; won → released; lost → no payout (an admin decides about one already sent).</item>
/// </list>
/// </summary>
public class SellerPayoutService(
    AuctionDbContext db,
    IDbContextOutbox<AuctionDbContext> outbox,
    IPaymentProvider provider,
    SellerPaymentService sellers,
    CheckoutService checkout,
    IOptions<PaymentOptions> options,
    TimeProvider clock,
    ILogger<SellerPayoutService> logger)
{
    private readonly PaymentOptions _opt = options.Value;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static readonly PaymentStatus[] Paid = [PaymentStatus.Succeeded, PaymentStatus.PartiallyRefunded, PaymentStatus.Disputed, PaymentStatus.Refunded];

    /// <summary>The order's successful payment (also after refunds / a dispute).</summary>
    public Task<Payment?> PaymentOfAsync(string orderId, CancellationToken ct) =>
        db.Payments.Where(p => p.OrderId == orderId && Paid.Contains(p.Status)).OrderByDescending(p => p.PaidAt).FirstOrDefaultAsync(ct);

    /// <summary>The seller's share in the payment's currency: item + shipping − commission − refunds (never below 0).</summary>
    public static long SellerShare(Payment p) => Math.Max(0, p.TotalAmount - p.RefundedAmount - p.CommissionAmount);

    private static bool DisputeOpen(Payment p) => p.ProviderDisputeId is not null && p.DisputeStatus is not ("won" or "warning_closed");

    // ── order steps ─────────────────────────────────────────────────────

    /// <summary>Picked up / Shipped: the order completes by itself after Payments:AutoCompleteDays (paid online only). Caller saves.</summary>
    public void OnHandedOver(Order order, DateTime now)
    {
        if (order.PaymentStatus is not OrderPaymentStatus.Paid) return;
        order.AutoCompleteAt ??= now + _opt.AutoCompleteWindow;
    }

    /// <summary>
    /// The order just became Completed: its payout is created (sent after the caller saves, with <see cref="SendForOrderAsync"/>).
    /// The buyer confirming also ends a problem they reported themselves. Caller saves.
    /// </summary>
    public async Task<PaymentTransfer?> OnCompletedAsync(Order order, string by, CancellationToken ct)
    {
        order.AutoCompleteAt = null;
        if (by == order.Buyer && order.HoldReason?.StartsWith(BuyerProblemPrefix) == true)
        {
            order.OnHoldSince = null;
            order.HoldReason = null;
        }
        if (await PaymentOfAsync(order.Id, ct) is not { } payment) return null;
        var existing = await db.PaymentTransfers.FirstOrDefaultAsync(t => t.OrderId == order.Id, ct);
        if (existing is not null) return existing;

        var transfer = new PaymentTransfer
        {
            OrderId = order.Id, PaymentId = payment.Id, Seller = order.Seller, Currency = _opt.PlatformCurrency.ToUpperInvariant(),
            SellerAmount = SellerShare(payment), SellerCurrency = payment.Currency, Status = TransferStatus.Pending, CreatedAt = Now,
        };
        if (transfer.SellerAmount == 0) transfer.Status = TransferStatus.Cancelled;
        db.PaymentTransfers.Add(transfer);
        return transfer;
    }

    /// <summary>Sends the order's payout if it is due (after the order was saved as Completed).</summary>
    public async Task SendForOrderAsync(string orderId, CancellationToken ct)
    {
        if (await db.PaymentTransfers.FirstOrDefaultAsync(t => t.OrderId == orderId, ct) is { } transfer)
            await SendAsync(transfer, ct);
    }

    /// <summary>
    /// Transfers the seller's share. Stays Pending (retried) when the seller's account isn't Active or the provider fails;
    /// Frozen while the order is on hold or disputed. Saves.
    /// </summary>
    public async Task SendAsync(PaymentTransfer transfer, CancellationToken ct)
    {
        if (transfer.Status is not (TransferStatus.Pending or TransferStatus.Frozen)) return;
        var order = await db.Orders.FirstAsync(o => o.Id == transfer.OrderId, ct);
        var payment = await db.Payments.FirstAsync(p => p.Id == transfer.PaymentId, ct);
        var now = Now;

        transfer.SellerAmount = SellerShare(payment);
        transfer.SellerCurrency = payment.Currency;
        if (transfer.SellerAmount == 0)
        {
            transfer.Status = TransferStatus.Cancelled;
            transfer.FailureMessage = "Refunded – nothing to pay out";
            await db.SaveChangesAsync(ct);
            return;
        }
        if (order.OnHoldSince is not null || DisputeOpen(payment))
        {
            transfer.Status = TransferStatus.Frozen;
            transfer.FailureMessage = order.HoldReason ?? "The buyer's bank is disputing the payment";
            await db.SaveChangesAsync(ct);
            return;
        }
        transfer.Status = TransferStatus.Pending;

        var account = await sellers.FindAsync(transfer.Seller, ct);
        if (account is null || account.Status != SellerPaymentStatus.Active)
        {
            transfer.FailureMessage = "Waiting for the seller's payment setup (Profile → Payments)";
            await db.SaveChangesAsync(ct);
            return;
        }

        transfer.Attempts++;
        transfer.LastAttemptAt = now;
        transfer.ProviderAccountId = account.ProviderAccountId;
        try
        {
            transfer.Amount = await InPlatformCurrencyAsync(payment, transfer.SellerAmount, ct);
            var result = await provider.CreateTransferAsync(new TransferRequest(
                transfer.Id, transfer.Amount, transfer.Currency, account.ProviderAccountId, payment.ProviderChargeId,
                payment.TransferGroup ?? $"order_{order.Id}", $"ShopCarParts order {order.Id}: {order.Title}",
                new Dictionary<string, string> { ["order_id"] = order.Id, ["seller"] = order.Seller, ["payment_id"] = payment.Id }), ct);
            transfer.ProviderTransferId = result.Id;
            transfer.ArrivedAmount = result.ArrivedAmount;
            transfer.ArrivedCurrency = result.ArrivedCurrency;
            transfer.Status = TransferStatus.Transferred;
            transfer.TransferredAt = now;
            transfer.FailureMessage = null;
            await NotifyAsync(order, order.Seller, $"Payment sent: {PayoutText(transfer)}");
            logger.LogInformation("Order {OrderId}: {Amount} {Currency} transferred to {Seller} ({TransferId})",
                order.Id, transfer.Amount, transfer.Currency, order.Seller, result.Id);
        }
        catch (PaymentProviderException e)
        {
            transfer.FailureMessage = e.Message.Length > 300 ? e.Message[..300] : e.Message;
            logger.LogWarning("Order {OrderId}: payout not sent (attempt {Attempt}): {Message}", order.Id, transfer.Attempts, e.Message);
        }
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
    }

    /// <summary>
    /// The share in the platform's balance currency. Same currency: as is. EUR on a US platform: at the rate the charge was
    /// converted with (read from the provider when not stored yet).
    /// </summary>
    private async Task<long> InPlatformCurrencyAsync(Payment payment, long amount, CancellationToken ct)
    {
        var platform = _opt.PlatformCurrency.ToUpperInvariant();
        if (string.Equals(payment.Currency, platform, StringComparison.OrdinalIgnoreCase)) return amount;
        if (payment.ExchangeRate is null) await checkout.ReadChargeAsync(payment, ct);
        if (payment.ExchangeRate is not { } rate || !string.Equals(payment.BalanceCurrency, platform, StringComparison.OrdinalIgnoreCase))
            throw new PaymentProviderException("The payment's currency conversion isn't known yet – retrying later");
        // Never more than the charge brought in.
        return Math.Min((long)Math.Round(amount * rate, 0, MidpointRounding.ToZero), payment.BalanceAmount ?? long.MaxValue);
    }

    // ── job ──────────────────────────────────────────────────────────────

    /// <summary>Auto-completes handed-over orders whose time came, and (re)sends due payouts.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var now = Now;
        var due = await db.Orders.Include(o => o.Events)
            .Where(o => o.AutoCompleteAt != null && o.AutoCompleteAt <= now && o.OnHoldSince == null &&
                        o.PaymentStatus == OrderPaymentStatus.Paid &&
                        (o.Status == OrderStatus.PickedUp || o.Status == OrderStatus.Shipped || o.Status == OrderStatus.ReadyForPickup))
            .ToListAsync(ct);
        foreach (var order in due)
        {
            var days = _opt.AutoCompleteMinutes is > 0 ? $"{_opt.AutoCompleteMinutes} min" : $"{_opt.AutoCompleteDays} days";
            await CompleteAsync(order, "system", $"Completed automatically {days} after {(order.Status == OrderStatus.Shipped ? "shipping" : "the handover")}", now, ct);
            await NotifyAsync(order, order.Buyer, "Order completed automatically – the seller has been paid");
        }
        if (due.Count > 0) await outbox.SaveChangesAndFlushMessagesAsync(ct);

        // Pending payouts: new ones at once, failed ones again after 1, 4, 9 … minutes (at most hourly).
        var pending = await db.PaymentTransfers.Where(t => t.Status == TransferStatus.Pending || t.Status == TransferStatus.Frozen).ToListAsync(ct);
        foreach (var transfer in pending)
        {
            var wait = TimeSpan.FromMinutes(Math.Min(transfer.Attempts * transfer.Attempts, 60));
            if (transfer.LastAttemptAt is { } last && last + wait > now) continue;
            var order = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == transfer.OrderId, ct);
            if (order.Status != OrderStatus.Completed) continue;
            await SendAsync(transfer, ct);
        }
    }

    /// <summary>Marks the order Completed (by the job or an admin) and creates its payout. Caller saves, then sends.</summary>
    private async Task CompleteAsync(Order order, string by, string note, DateTime now, CancellationToken ct)
    {
        order.Status = OrderStatus.Completed;
        order.HandedOverAt ??= now;
        order.CompletedAt = now;
        order.Events.Add(new OrderEvent { OrderId = order.Id, Status = OrderStatus.Completed, At = now, By = by, Note = note });
        await outbox.PublishAsync(new Handlers.ReviewReminder(order.Id), new Wolverine.DeliveryOptions { ScheduledTime = now + ReviewService.ReminderAfter });
        await OnCompletedAsync(order, by, ct);
    }

    // ── holds ────────────────────────────────────────────────────────────

    public const string BuyerProblemPrefix = "Buyer reported a problem";

    /// <summary>The buyer reports a problem before completing: no auto-complete, no payout, until it's sorted out.</summary>
    public async Task<Order> ReportProblemAsync(string buyer, string orderId, string? text, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (order.Buyer != buyer) throw new CheckoutException("Only the buyer can report a problem", "NOT_BUYER", 403);
        if (order.PaymentStatus != OrderPaymentStatus.Paid) throw new CheckoutException("Only orders paid online can be put on hold", "NOT_PAID");
        if (order.Status is OrderStatus.Completed or OrderStatus.Cancelled)
            throw new CheckoutException("This order is already closed – contact support", "ORDER_CLOSED", 409);
        var detail = string.IsNullOrWhiteSpace(text) ? "" : $": {text.Trim()}";
        await HoldAsync(order, Truncate($"{BuyerProblemPrefix}{detail}", 300), buyer, ct);
        await NotifyAsync(order, order.Seller, "The buyer reported a problem – payment on hold, please message them");
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
        return order;
    }

    public async Task<Order> AdminHoldAsync(string admin, string orderId, string? reason, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        await HoldAsync(order, Truncate($"On hold by support{(string.IsNullOrWhiteSpace(reason) ? "" : $": {reason.Trim()}")}", 300), admin, ct);
        await NotifyAsync(order, order.Seller, "Payment on hold – support is reviewing this order");
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
        return order;
    }

    /// <summary>Ends a hold; with <paramref name="complete"/> the order is completed now (and the seller paid).</summary>
    public async Task<Order> AdminReleaseAsync(string admin, string orderId, bool complete, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        order.OnHoldSince = null;
        order.HoldReason = null;
        var now = Now;
        order.Events.Add(new OrderEvent { OrderId = order.Id, Status = order.Status, At = now, By = admin, Note = "Hold released by support" });
        if (complete && order.Status is not (OrderStatus.Completed or OrderStatus.Cancelled or OrderStatus.AwaitingPayment))
        {
            await CompleteAsync(order, admin, "Completed by support", now, ct);
            await NotifyAsync(order, order.Buyer, "Order completed by support");
        }
        await NotifyAsync(order, order.Seller, order.Status == OrderStatus.Completed ? "Hold released – the payment is on its way" : "Hold released");
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
        await SendForOrderAsync(order.Id, ct);
        return order;
    }

    private async Task HoldAsync(Order order, string reason, string by, CancellationToken ct)
    {
        var now = Now;
        order.OnHoldSince ??= now;
        order.HoldReason = reason;
        order.Events.Add(new OrderEvent { OrderId = order.Id, Status = order.Status, At = now, By = by, Note = reason });
        if (await db.PaymentTransfers.FirstOrDefaultAsync(t => t.OrderId == order.Id && t.Status == TransferStatus.Pending, ct) is { } transfer)
        {
            transfer.Status = TransferStatus.Frozen;
            transfer.FailureMessage = reason;
        }
    }

    // ── refunds ──────────────────────────────────────────────────────────

    /// <summary>
    /// The seller cancels a paid order they can't fulfil (before pickup / shipping): everything goes back to the buyer.
    /// </summary>
    public async Task<Order> SellerCancelAsync(string seller, string orderId, string? reason, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (order.Seller != seller) throw new CheckoutException("Only the seller can cancel this order", "NOT_SELLER", 403);
        if (order.PaymentStatus == OrderPaymentStatus.Refunded) throw new CheckoutException("This order is already refunded", "ALREADY_REFUNDED", 409);
        if (order.PaymentStatus != OrderPaymentStatus.Paid) throw new CheckoutException("Only orders paid online are refunded here", "NOT_PAID");
        if (order.Status is not (OrderStatus.SoldAwaitingHandover or OrderStatus.ReadyForPickup))
            throw new CheckoutException("It's already picked up or shipped – ask support to refund", "ALREADY_HANDED_OVER", 409);
        var why = string.IsNullOrWhiteSpace(reason) ? "Cancelled by the seller" : $"Cancelled by the seller: {reason.Trim()}";
        return await RefundAsync(order, null, Truncate(why, 300), seller, ct);
    }

    /// <summary>Support refunds all (amount null) or part of an order (cents of the payment's currency).</summary>
    public async Task<Order> AdminRefundAsync(string admin, string orderId, long? amount, string? reason, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        return await RefundAsync(order, amount, Truncate(string.IsNullOrWhiteSpace(reason) ? "Refunded by support" : reason.Trim(), 300), admin, ct);
    }

    private async Task<Order> RefundAsync(Order order, long? amount, string reason, string by, CancellationToken ct)
    {
        var payment = await PaymentOfAsync(order.Id, ct) ?? throw new CheckoutException("This order has no online payment", "NOT_PAID");
        var left = payment.TotalAmount - payment.RefundedAmount;
        var refund = amount ?? left;
        if (left <= 0) throw new CheckoutException("This order is already refunded in full", "ALREADY_REFUNDED", 409);
        if (refund <= 0 || refund > left) throw new CheckoutException($"Refund between 0.01 and {left / 100m:0.00} {payment.Currency}", "AMOUNT");
        if (payment.ProviderPaymentId is not { } pi) throw new CheckoutException("This payment can't be refunded online", "NOT_PAID");

        var row = new PaymentRefund
        {
            PaymentId = payment.Id, OrderId = order.Id, Amount = refund, Currency = payment.Currency, Reason = reason, Status = "Pending", CreatedBy = by,
            CreatedAt = Now,
        };
        db.PaymentRefunds.Add(row);
        await db.SaveChangesAsync(ct);
        try
        {
            row.ProviderRefundId = await provider.RefundAsync(pi, refund == payment.TotalAmount ? null : refund, reason, $"refund-{row.Id}", ct);
            row.Status = "Succeeded";
        }
        catch (PaymentProviderException e)
        {
            row.Status = "Failed";
            await db.SaveChangesAsync(ct);
            throw new CheckoutException(e.Message, "REFUND_FAILED", 502);
        }
        await ApplyRefundAsync(order, payment, refund, reason, by, row.Id, ct);
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
        return order;
    }

    /// <summary>
    /// After money went back to the buyer: the payment, the order and the seller's payout follow. A payout already sent
    /// is reversed by the seller's part of the refund. Caller saves.
    /// </summary>
    private async Task ApplyRefundAsync(Order order, Payment payment, long refund, string reason, string by, string key, CancellationToken ct)
    {
        var now = Now;
        var shareBefore = SellerShare(payment);
        payment.RefundedAmount = Math.Min(payment.TotalAmount, payment.RefundedAmount + refund);
        var full = payment.RefundedAmount >= payment.TotalAmount;
        if (payment.Status != PaymentStatus.Disputed) payment.Status = full ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        payment.UpdatedAt = now;
        var shareAfter = SellerShare(payment);

        if (await db.PaymentTransfers.FirstOrDefaultAsync(t => t.OrderId == order.Id, ct) is { } transfer)
        {
            if (transfer.Status == TransferStatus.Transferred && transfer.ProviderTransferId is { } tr && shareBefore > shareAfter)
            {
                // The seller's part of the refund, in the transfer's currency (same rate as the payout).
                var back = Math.Min(transfer.Amount - transfer.ReversedAmount,
                    (long)Math.Ceiling((shareBefore - shareAfter) * (decimal)transfer.Amount / Math.Max(transfer.SellerAmount, 1)));
                if (back > 0)
                {
                    try
                    {
                        await provider.ReverseTransferAsync(tr, back, reason, $"rev-{key}", ct);
                        transfer.ReversedAmount += back;
                        if (transfer.ReversedAmount >= transfer.Amount) transfer.Status = TransferStatus.Reversed;
                    }
                    catch (PaymentProviderException e)
                    {
                        // The buyer has their money; support settles it with the seller.
                        transfer.FailureMessage = Truncate($"Refund {refund} not taken back from the payout: {e.Message}", 300);
                        logger.LogWarning("Order {OrderId}: transfer {TransferId} not reversed: {Message}", order.Id, tr, e.Message);
                    }
                }
            }
            else if (transfer.Status is TransferStatus.Pending or TransferStatus.Frozen)
            {
                transfer.SellerAmount = shareAfter;
                if (shareAfter == 0) transfer.Status = TransferStatus.Cancelled;
            }
        }

        var money = $"{refund / 100m:0.00} {payment.Currency}";
        if (full)
        {
            order.PaymentStatus = OrderPaymentStatus.Refunded;
            order.AutoCompleteAt = null;
            if (order.Status != OrderStatus.Completed)
            {
                order.Status = OrderStatus.Cancelled;
                order.CancelledAt = now;
                order.CancelReason = Truncate($"Refunded – {reason}", 200);
            }
        }
        order.Events.Add(new OrderEvent { OrderId = order.Id, Status = order.Status, At = now, By = by, Note = Truncate($"Refunded {money}: {reason}", 200) });
        await NotifyAsync(order, order.Buyer, full ? $"Refunded {money} to your card" : $"Partly refunded: {money} back to your card");
        await NotifyAsync(order, order.Seller, full ? $"Order refunded to the buyer ({money})" : $"{money} refunded to the buyer");
    }

    // ── webhooks ─────────────────────────────────────────────────────────

    /// <summary>charge.refunded: a refund made outside the app (Stripe Dashboard) is recorded like ours.</summary>
    public async Task<string> HandleChargeRefundedAsync(ChargeSnapshot charge, CancellationToken ct)
    {
        var payment = await FindAsync(charge.Id, charge.PaymentIntentId, ct);
        if (payment is null) return "unknown payment";
        // Our own refunds are written (Pending) before Stripe is called, so their webhook – which can arrive before the
        // refund's own transaction finished – is never taken for a second, Dashboard-made refund.
        var known = Math.Max(payment.RefundedAmount, await db.PaymentRefunds
            .Where(r => r.PaymentId == payment.Id && (r.Status == "Pending" || r.Status == "Succeeded")).SumAsync(r => r.Amount, ct));
        var extra = charge.AmountRefunded - known;
        if (extra <= 0) return "already recorded";
        if (payment.OrderId is null || await db.Orders.Include(o => o.Events).FirstOrDefaultAsync(o => o.Id == payment.OrderId, ct) is not { } order)
        {
            payment.RefundedAmount = charge.AmountRefunded;
            payment.Status = payment.RefundedAmount >= payment.TotalAmount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
            await db.SaveChangesAsync(ct);
            return "refund recorded";
        }
        var row = new PaymentRefund
        {
            PaymentId = payment.Id, OrderId = order.Id, Amount = extra, Currency = payment.Currency, Reason = "Refunded in the Stripe Dashboard",
            Status = "Succeeded", CreatedBy = "stripe", CreatedAt = Now,
        };
        db.PaymentRefunds.Add(row);
        await ApplyRefundAsync(order, payment, extra, row.Reason, "stripe", row.Id, ct);
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
        return "dashboard refund recorded";
    }

    /// <summary>charge.dispute.*: created / updated freeze the payout; closed won releases it, lost cancels it.</summary>
    public async Task<string> HandleDisputeAsync(string type, DisputeSnapshot dispute, CancellationToken ct)
    {
        var payment = await FindAsync(dispute.ChargeId, dispute.PaymentIntentId, ct);
        if (payment is null) return "unknown payment";
        var order = payment.OrderId is null ? null : await db.Orders.Include(o => o.Events).FirstOrDefaultAsync(o => o.Id == payment.OrderId, ct);
        var now = Now;
        payment.ProviderDisputeId = dispute.Id;
        payment.DisputeStatus = Truncate(dispute.Status, 40);
        payment.DisputeReason = dispute.Reason is null ? null : Truncate(dispute.Reason, 100);
        payment.UpdatedAt = now;
        var transfer = order is null ? null : await db.PaymentTransfers.FirstOrDefaultAsync(t => t.OrderId == order.Id, ct);
        string outcome;

        if (type == "charge.dispute.closed" && dispute.Status is "won" or "warning_closed")
        {
            payment.Status = payment.RefundedAmount > 0 ? PaymentStatus.PartiallyRefunded : PaymentStatus.Succeeded;
            if (order is not null && order.HoldReason == DisputeHold)
            {
                order.OnHoldSince = null;
                order.HoldReason = null;
                order.Events.Add(new OrderEvent { OrderId = order.Id, Status = order.Status, At = now, By = "stripe", Note = "Dispute won – hold released" });
                await NotifyAsync(order, order.Seller, "The buyer's dispute was closed in your favour – payment released");
            }
            outcome = "dispute won – released";
        }
        else if (type == "charge.dispute.closed" && dispute.Status == "lost")
        {
            payment.Status = PaymentStatus.Disputed;
            if (transfer is { Status: TransferStatus.Pending or TransferStatus.Frozen })
            {
                transfer.Status = TransferStatus.Cancelled;
                transfer.FailureMessage = "Dispute lost – the buyer's bank took the payment back";
            }
            if (order is not null)
            {
                order.PaymentStatus = OrderPaymentStatus.Refunded;
                order.AutoCompleteAt = null;
                if (order.HoldReason == DisputeHold) order.HoldReason = DisputeLost;
                order.Events.Add(new OrderEvent { OrderId = order.Id, Status = order.Status, At = now, By = "stripe", Note = "Dispute lost – payment returned to the buyer's bank" });
                await NotifyAsync(order, order.Seller, transfer?.Status == TransferStatus.Transferred
                    ? "The buyer's bank took the payment back – support will contact you about the payout"
                    : "The buyer's bank took the payment back – no payout for this order");
            }
            outcome = transfer?.Status == TransferStatus.Transferred ? "dispute lost – payout already sent, admin to decide" : "dispute lost – payout cancelled";
        }
        else
        {
            payment.Status = PaymentStatus.Disputed;
            if (order is not null && order.OnHoldSince is null)
            {
                order.OnHoldSince = now;
                order.HoldReason = DisputeHold;
                order.Events.Add(new OrderEvent { OrderId = order.Id, Status = order.Status, At = now, By = "stripe", Note = $"Payment disputed ({dispute.Reason ?? "no reason given"})" });
                await NotifyAsync(order, order.Seller, "The buyer's bank is disputing the payment – payout on hold");
            }
            if (transfer is { Status: TransferStatus.Pending })
            {
                transfer.Status = TransferStatus.Frozen;
                transfer.FailureMessage = DisputeHold;
            }
            outcome = $"dispute {dispute.Status} – payout on hold";
        }
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
        if (outcome.StartsWith("dispute won") && order?.Status == OrderStatus.Completed) await SendForOrderAsync(order.Id, ct);
        return outcome;
    }

    public const string DisputeHold = "The buyer's bank is disputing the payment";
    public const string DisputeLost = "Dispute lost – the buyer's bank took the payment back";

    /// <summary>transfer.reversed (also reversals made in the Dashboard): the stored amount follows.</summary>
    public async Task<string> HandleTransferAsync(TransferSnapshot snapshot, CancellationToken ct)
    {
        var transfer = await db.PaymentTransfers.FirstOrDefaultAsync(t => t.ProviderTransferId == snapshot.Id, ct);
        if (transfer is null) return "unknown transfer";
        if (snapshot.AmountReversed > transfer.ReversedAmount)
        {
            transfer.ReversedAmount = snapshot.AmountReversed;
            if (transfer.ReversedAmount >= transfer.Amount) transfer.Status = TransferStatus.Reversed;
            await db.SaveChangesAsync(ct);
            return "reversal recorded";
        }
        return "no change";
    }

    /// <summary>Is this charge / payment one of ours? (Disputes carry no metadata.)</summary>
    public async Task<bool> IsOursAsync(string? chargeId, string? paymentIntentId, CancellationToken ct) =>
        await FindAsync(chargeId, paymentIntentId, ct) is not null;

    private async Task<Payment?> FindAsync(string? chargeId, string? paymentIntentId, CancellationToken ct) =>
        (chargeId is null ? null : await db.Payments.FirstOrDefaultAsync(p => p.ProviderChargeId == chargeId, ct))
        ?? (paymentIntentId is null ? null : await db.Payments.FirstOrDefaultAsync(p => p.ProviderPaymentId == paymentIntentId, ct));

    // ── admin: payouts ───────────────────────────────────────────────────

    public async Task<PaymentTransfer> AdminRetryAsync(string transferId, CancellationToken ct)
    {
        var transfer = await db.PaymentTransfers.FirstOrDefaultAsync(t => t.Id == transferId, ct) ?? throw new CheckoutException("Payout not found", "NOT_FOUND", 404);
        if (transfer.Status is not (TransferStatus.Pending or TransferStatus.Frozen))
            throw new CheckoutException($"This payout is {transfer.Status}", "NOT_PENDING", 409);
        transfer.LastAttemptAt = null;
        await SendAsync(transfer, ct);
        return transfer;
    }

    /// <summary>Takes (part of) a sent payout back from the seller (e.g. after a lost dispute). Amount in the transfer's currency.</summary>
    public async Task<PaymentTransfer> AdminReverseAsync(string admin, string transferId, long? amount, string? reason, CancellationToken ct)
    {
        var transfer = await db.PaymentTransfers.FirstOrDefaultAsync(t => t.Id == transferId, ct) ?? throw new CheckoutException("Payout not found", "NOT_FOUND", 404);
        if (transfer.Status != TransferStatus.Transferred || transfer.ProviderTransferId is not { } tr)
            throw new CheckoutException("Only a sent payout can be taken back", "NOT_TRANSFERRED", 409);
        var left = transfer.Amount - transfer.ReversedAmount;
        var back = amount ?? left;
        if (back <= 0 || back > left) throw new CheckoutException($"Between 0.01 and {left / 100m:0.00} {transfer.Currency}", "AMOUNT");
        var why = string.IsNullOrWhiteSpace(reason) ? "Taken back by support" : Truncate(reason.Trim(), 300);
        try
        {
            await provider.ReverseTransferAsync(tr, back, why, $"rev-admin-{transfer.Id}-{transfer.ReversedAmount}-{back}", ct);
        }
        catch (PaymentProviderException e)
        {
            throw new CheckoutException(e.Message, "REVERSAL_FAILED", 502);
        }
        transfer.ReversedAmount += back;
        if (transfer.ReversedAmount >= transfer.Amount) transfer.Status = TransferStatus.Reversed;
        var order = await db.Orders.Include(o => o.Events).FirstAsync(o => o.Id == transfer.OrderId, ct);
        order.Events.Add(new OrderEvent { OrderId = order.Id, Status = order.Status, At = Now, By = admin, Note = Truncate($"Payout {back / 100m:0.00} {transfer.Currency} taken back: {why}", 200) });
        await NotifyAsync(order, order.Seller, $"{back / 100m:0.00} {transfer.Currency} of your payout was taken back: {why}");
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
        return transfer;
    }

    public async Task<List<AdminPaymentDto>> AdminListAsync(string? status, string? user, int take, CancellationToken ct)
    {
        var query = db.Payments.AsNoTracking().Where(p => p.Status != PaymentStatus.Pending && p.Status != PaymentStatus.Canceled);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PaymentStatus>(status, true, out var s)) query = query.Where(p => p.Status == s);
        if (!string.IsNullOrWhiteSpace(user)) query = query.Where(p => p.Buyer == user || p.Seller == user);
        var payments = await query.OrderByDescending(p => p.CreatedAt).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct);
        var orderIds = payments.Where(p => p.OrderId != null).Select(p => p.OrderId!).ToList();
        var orders = await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var transfers = await db.PaymentTransfers.AsNoTracking().Where(t => orderIds.Contains(t.OrderId)).ToListAsync(ct);
        return payments.Select(p => ToAdmin(p, p.OrderId is null ? null : orders.GetValueOrDefault(p.OrderId),
            transfers.FirstOrDefault(t => t.PaymentId == p.Id))).ToList();
    }

    public async Task<AdminOrderPaymentDto> AdminDetailAsync(string orderId, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct) ?? throw new CheckoutException("Order not found", "NOT_FOUND", 404);
        var payment = await db.Payments.AsNoTracking().Where(p => p.OrderId == orderId && Paid.Contains(p.Status)).FirstOrDefaultAsync(ct)
                      ?? throw new CheckoutException("This order has no online payment", "NOT_PAID", 404);
        var transfer = await db.PaymentTransfers.AsNoTracking().FirstOrDefaultAsync(t => t.OrderId == orderId, ct);
        var refunds = await db.PaymentRefunds.AsNoTracking().Where(r => r.PaymentId == payment.Id).OrderBy(r => r.CreatedAt).ToListAsync(ct);
        return new AdminOrderPaymentDto(ToAdmin(payment, order, transfer), refunds.Select(ToLine).ToList(), payment.ReceiptUrl,
            payment.ProviderPaymentId, payment.ProviderChargeId, transfer?.ProviderTransferId, payment.ProviderDisputeId, payment.ExchangeRate);
    }

    private static AdminPaymentDto ToAdmin(Payment p, Order? o, PaymentTransfer? t) => new(
        p.Id, p.OrderId, p.AuctionId, p.Buyer, p.Seller, p.Currency, p.TotalAmount, p.CommissionAmount, p.RefundedAmount, p.Status.ToString(),
        p.DisputeStatus, p.CreatedAt, p.PaidAt, o?.Status.ToString(), o?.OnHoldSince, o?.HoldReason, o?.AutoCompleteAt,
        t?.Id, t?.Status.ToString(), t?.Amount, t?.Currency, t?.SellerAmount, t?.SellerCurrency, t?.ArrivedAmount, t?.ArrivedCurrency, t?.FailureMessage);

    // ── what the app shows ───────────────────────────────────────────────

    /// <summary>Receipt (buyer) / payout statement (seller) for one order.</summary>
    public async Task<ReceiptDto> ReceiptAsync(string user, string orderId, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || (order.Buyer != user && order.Seller != user)) throw new CheckoutException("Order not found", "NOT_FOUND", 404);
        var payment = await db.Payments.AsNoTracking().Where(p => p.OrderId == orderId && Paid.Contains(p.Status)).FirstOrDefaultAsync(ct)
                      ?? throw new CheckoutException("This order wasn't paid online", "NOT_PAID", 404);
        var refunds = await db.PaymentRefunds.AsNoTracking().Where(r => r.PaymentId == payment.Id && r.Status == "Succeeded").OrderBy(r => r.CreatedAt).ToListAsync(ct);
        var seller = order.Seller == user;
        var transfer = seller ? await db.PaymentTransfers.AsNoTracking().FirstOrDefaultAsync(t => t.OrderId == orderId, ct) : null;
        return new ReceiptDto(order.Id, seller ? "seller" : "buyer", order.Title, order.Quantity, payment.Currency,
            payment.ItemAmount, payment.ShippingAmount, payment.TotalAmount, payment.PaidAt, seller ? null : payment.CardLabel,
            seller ? null : payment.ReceiptUrl, payment.RefundedAmount, refunds.Select(ToLine).ToList(),
            payment.CommissionPercent, seller ? payment.CommissionAmount : null, seller ? Payout(order, payment, transfer) : null,
            payment.DisputeStatus);
    }

    /// <summary>Payment details for a list of orders (receipt link for buyers, payout for sellers).</summary>
    public async Task<Dictionary<string, OrderPaymentInfo>> InfoForAsync(IReadOnlyCollection<Order> orders, string user, CancellationToken ct)
    {
        var ids = orders.Where(o => o.PaymentStatus is OrderPaymentStatus.Paid or OrderPaymentStatus.Refunded).Select(o => o.Id).ToList();
        if (ids.Count == 0) return [];
        var payments = await db.Payments.AsNoTracking().Where(p => p.OrderId != null && ids.Contains(p.OrderId) && Paid.Contains(p.Status)).ToListAsync(ct);
        var transfers = await db.PaymentTransfers.AsNoTracking().Where(t => ids.Contains(t.OrderId)).ToListAsync(ct);
        var result = new Dictionary<string, OrderPaymentInfo>();
        foreach (var order in orders)
        {
            if (payments.FirstOrDefault(p => p.OrderId == order.Id) is not { } payment) continue;
            var seller = order.Seller == user;
            result[order.Id] = new OrderPaymentInfo(seller ? null : payment.ReceiptUrl, payment.RefundedAmount,
                seller ? Payout(order, payment, transfers.FirstOrDefault(t => t.OrderId == order.Id)) : null);
        }
        return result;
    }

    /// <summary>
    /// "You'll receive $118.80 when the buyer confirms (or on 23 Oct)", "Paid out €94.04", "about €94.05" for EUR shares
    /// (sent in USD and converted by the provider).
    /// </summary>
    public PayoutView Payout(Order order, Payment payment, PaymentTransfer? transfer)
    {
        var share = transfer?.Status == TransferStatus.Transferred ? transfer.SellerAmount : SellerShare(payment);
        var converted = !string.Equals(payment.Currency, _opt.PlatformCurrency, StringComparison.OrdinalIgnoreCase);
        var amount = Money(share, payment.Currency, converted);
        var (status, label) = transfer?.Status switch
        {
            TransferStatus.Transferred => ("Transferred", $"Paid out {PayoutText(transfer)}"),
            TransferStatus.Reversed => ("Reversed", "Payout taken back"),
            TransferStatus.Cancelled => ("Cancelled", "No payout – refunded to the buyer"),
            TransferStatus.Frozen => ("OnHold", $"Payout of {amount} on hold"),
            TransferStatus.Pending when transfer.FailureMessage?.StartsWith("Waiting for the seller") == true =>
                ("WaitingForSetup", $"{amount} is waiting – finish your payment setup (Profile → Payments)"),
            TransferStatus.Pending => ("Sending", $"Sending {amount}"),
            _ when order.Status == OrderStatus.Cancelled || share == 0 => ("Cancelled", "No payout – refunded to the buyer"),
            _ when order.OnHoldSince is not null => ("OnHold", $"{amount} – on hold: {order.HoldReason}"),
            _ when order.AutoCompleteAt is { } at => ("Scheduled", $"You'll receive {amount} when the buyer confirms, or automatically on {at:d MMM}"),
            _ => ("AfterCompletion", $"You'll receive {amount} once the buyer confirms they have it"),
        };
        return new PayoutView(share, payment.Currency, converted && transfer?.ArrivedAmount is null, status, label, transfer?.TransferredAt);
    }

    /// <summary>
    /// What was paid out: the amount that arrived when known ("€94.04"), else the share ("about €94.05"); and what was
    /// taken back after a refund ("– $20.00 taken back").
    /// </summary>
    private string PayoutText(PaymentTransfer t)
    {
        var text = SentText(t);
        return t.ReversedAmount > 0 ? $"{text} – {Money(t.ReversedAmount, t.Currency, false)} taken back" : text;
    }

    private string SentText(PaymentTransfer t)
    {
        if (t.ArrivedAmount is { } arrived && t.ArrivedCurrency is { } currency)
            return string.Equals(currency, t.Currency, StringComparison.OrdinalIgnoreCase)
                ? Money(arrived, currency, false)
                : $"{Money(arrived, currency, false)} (sent as {Money(t.Amount, t.Currency, false)})";
        return Money(t.SellerAmount, t.SellerCurrency, !string.Equals(t.SellerCurrency, t.Currency, StringComparison.OrdinalIgnoreCase));
    }

    public static string Money(long cents, string currency, bool about)
    {
        var symbol = currency.ToUpperInvariant() switch { "USD" => "$", "EUR" => "€", _ => currency.ToUpperInvariant() + " " };
        return $"{(about ? "about " : "")}{symbol}{cents / 100m:0.00}";
    }

    private static RefundLineDto ToLine(PaymentRefund r) => new(r.Amount, r.Reason, r.Status, r.CreatedAt);

    // ── helpers ──────────────────────────────────────────────────────────

    private async Task<Order> LoadOrderAsync(string orderId, CancellationToken ct) =>
        await db.Orders.Include(o => o.Events).FirstOrDefaultAsync(o => o.Id == orderId, ct) ?? throw new CheckoutException("Order not found", "NOT_FOUND", 404);

    private Task NotifyAsync(Order order, string user, string label) =>
        outbox.PublishAsync(new OrderStatusChanged { OrderId = order.Id, AuctionId = order.AuctionId, Title = order.Title, StatusLabel = label, NotifyUser = user }).AsTask();

    private static string Truncate(string text, int max) => text.Length > max ? text[..max] : text;
}
