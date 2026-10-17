using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using AuctionService.Handlers;
using AuctionService.RequestHelpers;
using AuctionService.Services;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine.EntityFrameworkCore;

namespace AuctionService.Payments;

/// <summary>A refused checkout: the message is safe to show; Status is the HTTP status to answer with.</summary>
public class CheckoutException(string message, string code, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public class BuyNowRequest
{
    public string ListingId { get; set; } = "";
    public int Quantity { get; set; } = 1;
    /// <summary>Pickup or Shipping (with the address / delivery location, like an order's delivery choice).</summary>
    public ChooseDeliveryDto Delivery { get; set; } = new();
}

/// <summary>What the app needs to open the payment sheet, and the amounts the backend calculated (cents).</summary>
public record CheckoutSessionDto(
    string PaymentId,
    string ClientSecret,
    string CustomerId,
    string EphemeralKey,
    string? PublishableKey,
    string Currency,
    long ItemAmount,
    long ShippingAmount,
    long TotalAmount,
    // Buy now: the quantity is held until then.
    DateTime? ReservedUntil,
    // The order (auction / second chance), or the one a Buy now becomes once paid.
    string OrderId,
    string MerchantDisplayName = "ShopCarParts");

public record PaymentStatusDto(string PaymentId, string Status, string? OrderId, string? FailureMessage, DateTime? ReservedUntil);

/// <summary>
/// Buyer payments (USA / Ireland), separate charges and transfers: the buyer pays the PLATFORM; the seller's share is
/// transferred when the order is completed (Step 3). The amount is always calculated here – item price (or winning bid)
/// + the shipping charge (0 for pickup) – never sent by the app. An order becomes Paid only from the verified
/// payment_intent.succeeded webhook.
/// <list type="bullet">
/// <item>Buy now: the quantity is reserved for Payments:ReservationMinutes; paid → the order is created; failed / cancelled /
///   expired → the reservation is released and there is no order.</item>
/// <item>Auction win / second-chance offer: the order waits for payment (deadline); paid → fulfilment starts.</item>
/// </list>
/// </summary>
public class CheckoutService(
    AuctionDbContext db,
    IDbContextOutbox<AuctionDbContext> outbox,
    IPaymentProvider provider,
    SellerPaymentService sellers,
    ShippingService shipping,
    AuctionMapper mapper,
    IOptions<PaymentOptions> options,
    IOptions<StripeOptions> stripe,
    TimeProvider clock,
    ILogger<CheckoutService> logger)
{
    private readonly PaymentOptions _opt = options.Value;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ── Buy now ──────────────────────────────────────────────────────────

    public async Task<CheckoutSessionDto> StartBuyNowAsync(string buyer, string? email, string? phone, BuyNowRequest request, CancellationToken ct)
    {
        // ONE round trip for everything checkout needs: the listing and its item (no images, fitments, specs – the
        // snapshot for Search is built later, RefreshListingSnapshot), the block, the seller's payment status, the
        // buyer's earlier checkout of it and their Stripe customer. A remote database costs ~0.5 s per query.
        var found = await db.Auctions
            .Where(a => a.Id == request.ListingId)
            .Select(a => new
            {
                Auction = a,
                a.Item,
                a.Item.PartType,
                Blocked = db.UserBlocks.Any(b => b.Blocker == a.Seller && b.Blocked == buyer),
                SellerStatus = db.SellerPaymentAccounts.Where(s => s.Seller == a.Seller).Select(s => (SellerPaymentStatus?)s.Status).FirstOrDefault(),
                Earlier = db.StockReservations.Where(r => r.AuctionId == a.Id && r.Buyer == buyer && r.Status == ReservationStatus.Active).ToList(),
                CustomerId = db.BuyerPaymentCustomers.Where(c => c.Buyer == buyer).Select(c => c.ProviderCustomerId).FirstOrDefault(),
            })
            .AsSingleQuery()
            .FirstOrDefaultAsync(ct)
            ?? throw new CheckoutException("This listing is no longer available", "NOT_FOUND", 404);
        var auction = found.Auction;
        if (auction.Status is Status.Draft or Status.Cancelled) throw new CheckoutException("This listing is no longer available", "NOT_FOUND", 404);
        if (auction.SaleType != SaleType.FixedPrice) throw new CheckoutException("This is an auction – place a bid instead", "NOT_FIXED_PRICE");
        if (auction.Seller == buyer) throw new CheckoutException("You can't buy your own listing", "OWN_LISTING");
        if (found.Blocked) throw new CheckoutException("You can't buy from this seller", "BLOCKED");
        if (!sellers.IsOnline(auction.Item.CountryCode))
            throw new CheckoutException("Online payment is coming soon in your country.", "PAYMENTS_NOT_AVAILABLE");
        if (!sellers.TestOverride && found.SellerStatus != SellerPaymentStatus.Active)
            throw new CheckoutException("Seller is finishing payment setup – you can buy as soon as it's done.", "SELLER_NOT_READY", 409);
        if (request.Quantity < 1) throw new CheckoutException("Buy at least 1", "QUANTITY");

        // A new checkout by the same buyer replaces their earlier one for this listing.
        foreach (var old in found.Earlier)
            await ReleaseAsync(old, auction, "replaced by a new checkout", ct);

        if (auction.Status != Status.Live || auction.QuantityAvailable == 0)
        {
            var reserved = auction.QuantityReserved > 0 && auction.QuantityListed - auction.QuantitySold > 0;
            throw new CheckoutException(reserved ? "Reserved – another buyer is paying for it right now. Try again in a few minutes." : "Sorry, this is sold out",
                reserved ? "RESERVED" : "SOLD_OUT", 409);
        }
        if (request.Quantity > auction.QuantityAvailable)
            throw new CheckoutException($"Only {auction.QuantityAvailable} available", "QUANTITY", 409);

        var options_ = OrderTracking.Normalize(auction.Item.DeliveryOptions);
        if (OrderTracking.ParseMethod(request.Delivery.Method) is not { } method || !options_.HasFlag(method))
            throw new CheckoutException("Choose one of the delivery options of this listing", "DELIVERY_REQUIRED");

        var now = Now;
        var reservation = new StockReservation
        {
            AuctionId = auction.Id, Buyer = buyer, Quantity = request.Quantity, Status = ReservationStatus.Active, CreatedAt = now,
            ExpiresAt = now.AddMinutes(Math.Max(_opt.ReservationMinutes, 1)), OrderId = Guid.NewGuid().ToString(),
            DeliveryMethod = method, BuyerEmail = email, BuyerPhone = phone,
        };
        if (method == DeliveryOptions.Shipping)
        {
            var check = await DeliveryChoice.ShippingAsync(db, shipping, buyer, request.Delivery, auction.Item.CountryCode, auction.Item,
                ShippingService.SizeOf(auction.Item), now);
            if (check.Errors is { Count: > 0 } errors)
                throw new CheckoutException(string.Join(" ", errors.SelectMany(e => e.Value)), "ADDRESS_INVALID");
            if (check.Choice is not { } choice) throw new CheckoutException(check.Message ?? "Shipping isn't possible", check.Code ?? "SHIPPING_NOT_AVAILABLE");
            reservation.ShipTo = choice.Address;
            reservation.ShippingFee = choice.Quote.Charge ?? 0;
            reservation.ShippingDistance = choice.Quote.Distance;
            reservation.ShippingDistanceUnit = choice.Quote.Unit;
            reservation.ShippingService = choice.Quote.Service;
            reservation.ShippingZone = choice.Quote.Zone;
            reservation.ShippingRateEstimated = choice.Quote.EstimatedRate;
        }

        // The buyer's Stripe customer (saved cards) is made before the reservation, so it's stored in the same save.
        var customerId = found.CustomerId ?? await NewCustomerAsync(buyer, email, ct);

        // Hold the quantity (a concurrency token: two buyers can't reserve the last unit).
        auction.QuantityReserved += request.Quantity;
        auction.UpdatedAt = now;
        db.StockReservations.Add(reservation);
        var payment = NewPayment(reservation.OrderId!, buyer, auction, auction.StartingPrice * request.Quantity, reservation.ShippingFee);
        payment.ReservationId = reservation.Id;
        db.Payments.Add(payment);
        // Search shows the quantity left ("Reserved" when none).
        await outbox.PublishAsync(new RefreshListingSnapshot(auction.Id));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CheckoutException("Someone else is buying it right now – check how many are left", "RESERVED", 409);
        }

        try
        {
            return await OpenSheetAsync(payment, buyer, email, auction, reservation.ExpiresAt, ct, customerId);
        }
        catch (PaymentProviderException)
        {
            // Stripe refused: give the quantity back at once.
            await ReleaseAsync(reservation, auction, "the payment couldn't be started", ct);
            payment.Status = PaymentStatus.Failed;
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
            throw;
        }
    }

    // ── Paying an order (auction win / second-chance offer) ─────────────

    public async Task<CheckoutSessionDto> StartOrderPaymentAsync(string buyer, string? email, string orderId, CancellationToken ct)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct) ?? throw new CheckoutException("Order not found", "NOT_FOUND", 404);
        if (order.Buyer != buyer) throw new CheckoutException("Only the buyer can pay for this order", "NOT_BUYER", 403);
        if (order.Status != OrderStatus.AwaitingPayment || order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
            throw new CheckoutException(order.PaymentStatus == OrderPaymentStatus.Paid ? "This order is already paid" : "This order can't be paid any more",
                "NOT_PAYABLE", 409);
        if (order.PaymentDueAt is { } due && due <= Now) throw new CheckoutException("The time to pay has passed", "DEADLINE_PASSED", 409);
        if (order.DeliveryMethod is null || OrderTracking.NeedsShippingAddress(order))
            throw new CheckoutException("Choose pickup or shipping first – the shipping charge is part of the total", "DELIVERY_REQUIRED");
        if (!await sellers.CanReceivePaymentsAsync(order.Seller, ct))
            throw new CheckoutException("Seller is finishing payment setup – you can pay as soon as it's done (your time to pay is shown on the order).",
                "SELLER_NOT_READY", 409);

        // Only the item: the payment's title and currency.
        var auction = await db.Auctions.Include(a => a.Item).FirstAsync(a => a.Id == order.AuctionId, ct);

        // An earlier attempt that wasn't finished is replaced (the amount may have changed with the delivery choice).
        foreach (var old in await db.Payments.Where(p => p.OrderId == order.Id && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Failed)).ToListAsync(ct))
        {
            if (old.ProviderPaymentId is { } pi) await provider.CancelPaymentIntentAsync(pi, ct);
            old.Status = PaymentStatus.Canceled;
            old.UpdatedAt = Now;
        }

        var shippingFee = order.DeliveryMethod == DeliveryOptions.Shipping ? order.ShippingFee ?? 0 : 0;
        var payment = NewPayment(order.Id, buyer, auction, order.FinalTotal, shippingFee);
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);
        return await OpenSheetAsync(payment, buyer, email, auction, null, ct);
    }

    /// <summary>The buyer closed the payment sheet (or it failed): the reservation is released at once.</summary>
    public async Task CancelAsync(string buyer, string paymentId, CancellationToken ct)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct) ?? throw new CheckoutException("Payment not found", "NOT_FOUND", 404);
        if (payment.Buyer != buyer) throw new CheckoutException("Payment not found", "NOT_FOUND", 404);
        if (payment.Status is not (PaymentStatus.Pending or PaymentStatus.Failed)) return;

        if (payment.ProviderPaymentId is { } pi) await provider.CancelPaymentIntentAsync(pi, ct);
        payment.Status = PaymentStatus.Canceled;
        payment.UpdatedAt = Now;
        if (payment.ReservationId is { } reservationId &&
            await db.StockReservations.FirstOrDefaultAsync(r => r.Id == reservationId && r.Status == ReservationStatus.Active, ct) is { } reservation)
        {
            var auction = await db.Auctions.Include(a => a.Item).FirstAsync(a => a.Id == reservation.AuctionId, ct);
            await ReleaseAsync(reservation, auction, "cancelled by the buyer", ct);
        }
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
    }

    public async Task<PaymentStatusDto> StatusAsync(string buyer, string paymentId, CancellationToken ct)
    {
        var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId && p.Buyer == buyer, ct)
                      ?? throw new CheckoutException("Payment not found", "NOT_FOUND", 404);
        DateTime? until = payment.ReservationId is { } r
            ? await db.StockReservations.Where(x => x.Id == r).Select(x => (DateTime?)x.ExpiresAt).FirstOrDefaultAsync(ct)
            : null;
        return new PaymentStatusDto(payment.Id, payment.Status.ToString(),
            payment.Status == PaymentStatus.Succeeded ? payment.OrderId : null, payment.FailureMessage, until);
    }

    // ── Webhooks ─────────────────────────────────────────────────────────

    /// <summary>payment_intent.succeeded (verified): the order is Paid (Buy now: created now).</summary>
    public async Task<string> HandleSucceededAsync(PaymentIntentSnapshot pi, CancellationToken ct)
    {
        var payment = await FindAsync(pi, ct);
        if (payment is null) return "unknown payment";
        if (payment.Status is PaymentStatus.Succeeded or PaymentStatus.Refunded || payment.PaidAt is not null) return "already handled";
        if (pi.Amount != payment.TotalAmount || !string.Equals(pi.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("Payment {PaymentId}: Stripe amount {Amount} {Currency} doesn't match {Expected} {ExpectedCurrency}",
                payment.Id, pi.Amount, pi.Currency, payment.TotalAmount, payment.Currency);
            return "amount mismatch – not applied";
        }

        var now = Now;
        payment.Status = PaymentStatus.Succeeded;
        payment.ProviderChargeId = pi.LatestChargeId;
        payment.PaidAt = now;
        payment.UpdatedAt = now;
        payment.FailureMessage = null;
        await ReadChargeAsync(payment, ct);

        var outcome = payment.ReservationId is not null
            ? await CompleteBuyNowAsync(payment, now, ct)
            : await CompleteOrderPaymentAsync(payment, now, ct);
        // The buyer's bank disputed it before this event arrived (Stripe doesn't guarantee the order of events): it stays
        // Disputed and the order starts on hold (no payout until the dispute is decided).
        if (payment.ProviderDisputeId is not null && payment.DisputeStatus is not ("won" or "warning_closed") && payment.Status == PaymentStatus.Succeeded)
        {
            payment.Status = PaymentStatus.Disputed;
            // A Buy now order was only just added (not saved yet).
            if ((db.Orders.Local.FirstOrDefault(o => o.Id == payment.OrderId) ?? await db.Orders.Include(o => o.Events).FirstOrDefaultAsync(o => o.Id == payment.OrderId, ct)) is { } order && order.OnHoldSince is null)
            {
                order.OnHoldSince = now;
                order.HoldReason = SellerPayoutService.DisputeHold;
                order.Events.Add(new OrderEvent { OrderId = order.Id, Status = order.Status, At = now, By = "stripe", Note = "Payment disputed" });
            }
            outcome += " – already disputed, on hold";
        }
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
        return outcome;
    }

    /// <summary>A declined card: noted; the buyer can try another card while the reservation / deadline lasts.</summary>
    public async Task<string> HandleFailedAsync(PaymentIntentSnapshot pi, CancellationToken ct)
    {
        var payment = await FindAsync(pi, ct);
        if (payment is null) return "unknown payment";
        if (payment.Status != PaymentStatus.Pending && payment.Status != PaymentStatus.Failed) return "already final";
        payment.Status = PaymentStatus.Failed;
        payment.FailureMessage = pi.FailureMessage is { Length: > 300 } m ? m[..300] : pi.FailureMessage;
        payment.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return "payment failed";
    }

    /// <summary>payment_intent.canceled: the reservation (if any) is released.</summary>
    public async Task<string> HandleCanceledAsync(PaymentIntentSnapshot pi, CancellationToken ct)
    {
        var payment = await FindAsync(pi, ct);
        if (payment is null) return "unknown payment";
        if (payment.Status is PaymentStatus.Succeeded or PaymentStatus.Refunded) return "already final";
        payment.Status = PaymentStatus.Canceled;
        payment.UpdatedAt = Now;
        if (payment.ReservationId is { } id && await db.StockReservations.FirstOrDefaultAsync(r => r.Id == id && r.Status == ReservationStatus.Active, ct) is { } reservation)
        {
            var auction = await db.Auctions.Include(a => a.Item).FirstAsync(a => a.Id == reservation.AuctionId, ct);
            await ReleaseAsync(reservation, auction, "payment cancelled", ct);
        }
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
        return "payment cancelled";
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// The receipt link, the card, and what the charge became in the platform's balance (a EUR payment is converted to
    /// USD: the seller's transfer uses that rate). Best effort – the payout reads it again if it's missing.
    /// </summary>
    public async Task ReadChargeAsync(Payment payment, CancellationToken ct)
    {
        if (payment.ProviderChargeId is not { } chargeId) return;
        try
        {
            var charge = await provider.GetChargeAsync(chargeId, ct);
            payment.ReceiptUrl = charge.ReceiptUrl is { Length: <= 500 } url ? url : payment.ReceiptUrl;
            payment.CardLabel = charge.CardLabel is { } card ? (card.Length > 60 ? card[..60] : card) : payment.CardLabel;
            if (charge.BalanceAmount is { } balance && charge.BalanceCurrency is { } currency)
            {
                payment.BalanceAmount = balance;
                payment.BalanceCurrency = currency;
                payment.ExchangeRate = charge.ExchangeRate;
            }
        }
        catch (PaymentProviderException e)
        {
            logger.LogWarning("Payment {PaymentId}: receipt / balance details not read yet: {Message}", payment.Id, e.Message);
        }
    }

    /// <summary>
    /// Gives the reserved quantity back (expired / cancelled / failed). The caller saves. Search and Bid get the listing's
    /// snapshot right after (RefreshListingSnapshot), so the auction only needs its item loaded here.
    /// </summary>
    public async Task ReleaseAsync(StockReservation reservation, Auction auction, string why, CancellationToken ct)
    {
        if (reservation.Status != ReservationStatus.Active) return;
        reservation.Status = ReservationStatus.Released;
        auction.QuantityReserved = Math.Max(auction.QuantityReserved - reservation.Quantity, 0);
        auction.UpdatedAt = Now;
        await outbox.PublishAsync(new RefreshListingSnapshot(auction.Id));
        logger.LogInformation("Reservation {ReservationId} of {Quantity} × {AuctionId} released: {Why}", reservation.Id, reservation.Quantity, auction.Id, why);
    }

    private Payment NewPayment(string orderId, string buyer, Auction auction, int itemWhole, int shippingWhole)
    {
        var amounts = PaymentAmounts.For(PaymentAmounts.ToMinor(itemWhole), PaymentAmounts.ToMinor(shippingWhole), _opt.CommissionPercent);
        var now = Now;
        return new Payment
        {
            OrderId = orderId, AuctionId = auction.Id, Buyer = buyer, Seller = auction.Seller, Provider = provider.Name,
            Currency = (auction.Item.Currency ?? RegionRules.CurrencyFor(auction.Item.CountryCode)).ToUpperInvariant(),
            ItemAmount = amounts.Item, ShippingAmount = amounts.Shipping, TotalAmount = amounts.Total,
            CommissionAmount = amounts.Commission, CommissionPercent = _opt.CommissionPercent,
            Status = PaymentStatus.Pending, TransferGroup = $"order_{orderId}", CreatedAt = now, UpdatedAt = now,
        };
    }

    private async Task<CheckoutSessionDto> OpenSheetAsync(Payment payment, string buyer, string? email, Auction auction, DateTime? reservedUntil,
        CancellationToken ct, string? knownCustomerId = null)
    {
        var customerId = knownCustomerId ?? await CustomerIdAsync(buyer, email, ct);
        var intent = await provider.CreatePaymentIntentAsync(new PaymentIntentRequest(
            payment.Id, payment.TotalAmount, payment.Currency, customerId, payment.TransferGroup!,
            Truncate($"ShopCarParts: {auction.Item.Title ?? auction.Item.DisplayName}", 200), email,
            new Dictionary<string, string>
            {
                ["order_id"] = payment.OrderId ?? "", ["listing_id"] = auction.Id, ["buyer"] = buyer, ["seller"] = auction.Seller,
                ["kind"] = payment.ReservationId is null ? "order" : "buy_now",
            }), ct);
        payment.ProviderPaymentId = intent.Id;
        payment.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        var key = await provider.CreateCustomerSessionKeyAsync(customerId, ct);
        return new CheckoutSessionDto(payment.Id, intent.ClientSecret, customerId, key, stripe.Value.PublishableKey, payment.Currency,
            payment.ItemAmount, payment.ShippingAmount, payment.TotalAmount, reservedUntil, payment.OrderId!);
    }

    private async Task<string> CustomerIdAsync(string buyer, string? email, CancellationToken ct)
    {
        var existing = await db.BuyerPaymentCustomers.FirstOrDefaultAsync(c => c.Buyer == buyer, ct);
        if (existing is not null) return existing.ProviderCustomerId;
        var id = await NewCustomerAsync(buyer, email, ct);
        await db.SaveChangesAsync(ct);
        return id;
    }

    /// <summary>Creates the buyer's customer at the provider and adds it (the caller saves).</summary>
    private async Task<string> NewCustomerAsync(string buyer, string? email, CancellationToken ct)
    {
        var id = await provider.CreateCustomerAsync(buyer, email, ct);
        db.BuyerPaymentCustomers.Add(new BuyerPaymentCustomer { Buyer = buyer, Provider = provider.Name, ProviderCustomerId = id });
        return id;
    }

    private async Task<Payment?> FindAsync(PaymentIntentSnapshot pi, CancellationToken ct) =>
        await db.Payments.FirstOrDefaultAsync(p => p.ProviderPaymentId == pi.Id, ct)
        ?? (pi.PaymentId is { } id ? await db.Payments.FirstOrDefaultAsync(p => p.Id == id, ct) : null);

    /// <summary>Buy now paid: the order is created with what the buyer chose, the quantity is sold.</summary>
    private async Task<string> CompleteBuyNowAsync(Payment payment, DateTime now, CancellationToken ct)
    {
        var reservation = await db.StockReservations.FirstAsync(r => r.Id == payment.ReservationId, ct);
        var auction = await db.Auctions.WithDetails().FirstAsync(a => a.Id == reservation.AuctionId, ct);

        if (reservation.Status == ReservationStatus.Active)
        {
            auction.QuantityReserved = Math.Max(auction.QuantityReserved - reservation.Quantity, 0);
        }
        else if (auction.QuantityAvailable < reservation.Quantity || auction.Status != Status.Live)
        {
            // Paid after the reservation expired and someone else bought it: the money goes straight back.
            var refundId = await provider.RefundAsync(payment.ProviderPaymentId!, null, "sold out after the reservation expired", $"refund-{payment.Id}-late", ct);
            payment.Status = PaymentStatus.Refunded;
            payment.RefundedAmount = payment.TotalAmount;
            db.PaymentRefunds.Add(new PaymentRefund
            {
                PaymentId = payment.Id, OrderId = reservation.OrderId ?? "", ProviderRefundId = refundId, Amount = payment.TotalAmount,
                Currency = payment.Currency, Reason = "Sold out after the reservation expired", Status = "Succeeded", CreatedBy = "system",
            });
            logger.LogWarning("Payment {PaymentId} arrived after its reservation expired and the item was gone: refunded", payment.Id);
            return "paid too late – refunded";
        }

        reservation.Status = ReservationStatus.Converted;
        auction.QuantitySold += reservation.Quantity;
        auction.UpdatedAt = now;
        if (auction.QuantitySold >= auction.QuantityListed)
        {
            auction.Status = Status.Sold;
            auction.Winner = reservation.Buyer;
            auction.SoldAmount = auction.StartingPrice;
        }

        var order = OrderTracking.NewOrder(auction, reservation.Buyer, reservation.Quantity, auction.StartingPrice * reservation.Quantity, mapper.CoverUrl(auction));
        order.Id = reservation.OrderId!;
        order.CreatedAt = now;
        order.BuyerEmail = reservation.BuyerEmail;
        order.BuyerPhone = reservation.BuyerPhone;
        order.DeliveryMethod = reservation.DeliveryMethod;
        if (reservation.DeliveryMethod == DeliveryOptions.Shipping)
        {
            order.ShipTo = reservation.ShipTo;
            order.DeliveryAddress = reservation.ShipTo?.OneLine();
            order.ShippingFee = reservation.ShippingFee;
            order.ShippingDistance = reservation.ShippingDistance;
            order.ShippingDistanceUnit = reservation.ShippingDistanceUnit;
            order.ShippingService = reservation.ShippingService;
            order.ShippingZone = reservation.ShippingZone;
            order.ShippingRateEstimated = reservation.ShippingRateEstimated;
        }
        order.PaymentStatus = OrderPaymentStatus.Paid;
        order.PaidAt = now;
        db.Orders.Add(order);
        payment.OrderId = order.Id;

        await outbox.PublishAsync(mapper.ToUpdated(auction));
        await outbox.PublishAsync(new OrderCreated { OrderId = order.Id, AuctionId = auction.Id, Seller = auction.Seller, Buyer = order.Buyer });
        await outbox.PublishAsync(new OrderStatusChanged
        {
            OrderId = order.Id, AuctionId = auction.Id, Title = order.Title, NotifyUser = auction.Seller,
            StatusLabel = reservation.Quantity > 1 ? $"New order: {reservation.Quantity} units bought and paid" : "New order: bought and paid",
        });
        await outbox.PublishAsync(new OrderStatusChanged
        {
            OrderId = order.Id, AuctionId = auction.Id, Title = order.Title, NotifyUser = order.Buyer, StatusLabel = "Payment received – thank you!",
        });
        return "buy now paid – order created";
    }

    /// <summary>Auction win / second-chance order paid: fulfilment starts.</summary>
    private async Task<string> CompleteOrderPaymentAsync(Payment payment, DateTime now, CancellationToken ct)
    {
        var order = await db.Orders.FirstAsync(o => o.Id == payment.OrderId, ct);
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            // Paid after the deadline closed the order (or it was cancelled): the money goes straight back.
            var refundId = await provider.RefundAsync(payment.ProviderPaymentId!, null, "the order was closed before the payment arrived", $"refund-{payment.Id}-late", ct);
            payment.Status = PaymentStatus.Refunded;
            payment.RefundedAmount = payment.TotalAmount;
            db.PaymentRefunds.Add(new PaymentRefund
            {
                PaymentId = payment.Id, OrderId = order.Id, ProviderRefundId = refundId, Amount = payment.TotalAmount, Currency = payment.Currency,
                Reason = "Paid after the order was closed", Status = "Succeeded", CreatedBy = "system",
            });
            return "paid after the order closed – refunded";
        }

        order.Status = OrderStatus.SoldAwaitingHandover;
        order.PaymentStatus = OrderPaymentStatus.Paid;
        order.PaidAt = now;
        order.Events.Add(new OrderEvent { OrderId = order.Id, Status = OrderStatus.SoldAwaitingHandover, At = now, By = order.Buyer, Note = "Paid online" });

        if (order.SecondChanceRound > 0)
        {
            // The next bidder bought it: they are the winner now.
            var auction = await db.Auctions.WithDetails().FirstAsync(a => a.Id == order.AuctionId, ct);
            auction.Winner = order.Buyer;
            auction.SoldAmount = order.FinalTotal;
            auction.Status = Status.Sold;
            auction.UpdatedAt = now;
            if (await db.SecondChanceOffers.FirstOrDefaultAsync(o => o.OrderId == order.Id, ct) is { } offer) offer.Status = SecondChanceStatus.Paid;
            await outbox.PublishAsync(mapper.ToUpdated(auction));
        }

        await outbox.PublishAsync(new OrderStatusChanged
        {
            OrderId = order.Id, AuctionId = order.AuctionId, Title = order.Title, NotifyUser = order.Seller,
            StatusLabel = order.DeliveryMethod == DeliveryOptions.Shipping ? "Paid – ship it" : "Paid – get it ready for pickup",
        });
        await outbox.PublishAsync(new OrderStatusChanged
        {
            OrderId = order.Id, AuctionId = order.AuctionId, Title = order.Title, NotifyUser = order.Buyer, StatusLabel = "Payment received – thank you!",
        });
        return "order paid";
    }

    private static string Truncate(string text, int max) => text.Length > max ? text[..max] : text;
}
