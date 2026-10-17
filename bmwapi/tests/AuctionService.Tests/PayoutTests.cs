using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Alba;
using AuctionService.Controllers;
using AuctionService.Data;
using AuctionService.Entities;
using AuctionService.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuctionService.Tests;

/// <summary>
/// Step 3 against the fake provider: the seller is paid (a transfer from the buyer's charge) only when the order is
/// completed – by the buyer or automatically 7 days after pickup / shipping; Irish sellers get "about €…" (the USD
/// transfer is converted); holds, refunds (seller / admin / Stripe Dashboard), disputes, receipts and the admin tools.
/// </summary>
[Collection("payments")]
public class PayoutTests(PaymentsFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private Task<IScenarioResult> Call(Action<Scenario> configure, string user, HttpStatusCode status = HttpStatusCode.OK, string role = "buyer") =>
        fixture.Host.Scenario(s =>
        {
            s.RemoveClaim("role");
            s.WithClaim(new Claim("role", role));
            s.WithClaim(new Claim("username", user));
            s.WithClaim(new Claim("email", $"{user}@example.com"));
            configure(s);
            s.StatusCodeShouldBe(status);
        });

    private static async Task<T> Read<T>(IScenarioResult r) => JsonSerializer.Deserialize<T>(await r.ReadAsTextAsync(), Json)!;

    private async Task<T> Db<T>(Func<AuctionDbContext, Task<T>> query)
    {
        using var scope = fixture.Host.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AuctionDbContext>());
    }

    private static string Id() => Guid.NewGuid().ToString();
    private DateTime Now => fixture.Clock.GetUtcNow().UtcDateTime;

    /// <summary>A live fixed-price listing (US: Houston, USD / IE: Dublin, EUR) of a seller whose payment account is Active.</summary>
    private async Task<string> Listing(string seller, int price, string country = "US", bool sellerActive = true)
    {
        var id = Id();
        var (lat, lon, place, currency) = country == "IE" ? (53.3498, -6.2603, "Dublin", "EUR") : (29.7604, -95.3698, "Houston, TX", "USD");
        await Db(async db =>
        {
            db.Auctions.Add(new Auction
            {
                Id = id, Seller = seller, Status = Status.Live, SaleType = SaleType.FixedPrice, StartingPrice = price, ReservePrice = price,
                AuctionEnd = DateTime.UtcNow.AddYears(1),
                Item = new Item
                {
                    Id = id, Kind = ListingKind.CommonPart, ItemType = ItemType.Part, Make = "", Model = "", Description = "Test part",
                    Title = $"Dash cam {id[..4]}", CountryCode = country, Currency = currency, LocationName = place,
                    Latitude = lat, Longitude = lon, DeliveryOptions = DeliveryOptions.Pickup | DeliveryOptions.Shipping, Quantity = 1,
                },
            });
            if (!await db.SellerPaymentAccounts.AnyAsync(a => a.Seller == seller))
                db.SellerPaymentAccounts.Add(new SellerPaymentAccount
                {
                    Seller = seller, ProviderAccountId = $"acct_{country}_{seller}", Country = country,
                    Status = sellerActive ? SellerPaymentStatus.Active : SellerPaymentStatus.Pending,
                    PayoutsEnabled = sellerActive, ChargesEnabled = sellerActive, TransfersActive = sellerActive, DetailsSubmitted = true,
                });
            await db.SaveChangesAsync();
            return 0;
        });
        return id;
    }

    /// <summary>Buy now (pickup) and Stripe's payment_intent.succeeded: a paid order. Returns (order id, payment intent id).</summary>
    private async Task<(string Order, string Pi)> PaidOrder(string seller, string buyer, int price, string country = "US", bool sellerActive = true)
    {
        var listing = await Listing(seller, price, country, sellerActive);
        if (!sellerActive) fixture.Provider.IsTestMode = true; // the test-only override lets buyers pay an inactive seller
        try
        {
            var session = await Read<CheckoutSessionDto>(await Call(s => s.Post.Json(new { listingId = listing, quantity = 1, delivery = new { method = "Pickup" } })
                .ToUrl("/api/payments/checkout/buy-now"), buyer));
            var pi = fixture.Provider.Intents.Single(i => i.Value.Request.PaymentId == session.PaymentId).Key;
            Assert.Contains("buy now paid", await fixture.WebhookAsync(fixture.PaymentEvent($"evt_{Id()}", "payment_intent.succeeded", pi)));
            return (session.OrderId, pi);
        }
        finally
        {
            fixture.Provider.IsTestMode = false;
        }
    }

    private Task<IScenarioResult> Move(string order, string status, string user, string role, HttpStatusCode code = HttpStatusCode.OK) =>
        Call(s => s.Post.Json(new { status }).ToUrl($"/api/orders/{order}/status"), user, code, role);

    private async Task<OrderDto> OrderAs(string order, string user, string role = "buyer") =>
        await Read<OrderDto>(await Call(s => s.Get.Url($"/api/orders/{order}"), user, role: role));

    private async Task<PaymentTransfer?> Transfer(string order) => await Db(db => db.PaymentTransfers.AsNoTracking().FirstOrDefaultAsync(t => t.OrderId == order));
    private async Task<Payment> PaymentOf(string order) => await Db(db => db.Payments.AsNoTracking().SingleAsync(p => p.OrderId == order && p.Status != PaymentStatus.Canceled));
    private async Task<Order> OrderRow(string order) => await Db(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == order));

    // ── payout on completion ─────────────────────────────────────────────

    [Fact]
    public async Task BuyerConfirms_TheSellerIsPaid_ItemMinusCommission_FromTheBuyersCharge()
    {
        var (order, pi) = await PaidOrder("seller.po1", "buyer.po1", 120);

        // Paid but not completed: nothing is sent; the seller sees what they'll get.
        var sellerView = await OrderAs(order, "seller.po1", "seller");
        Assert.Equal(("AfterCompletion", 11880L, "USD", false), (sellerView.Payout!.Status, sellerView.Payout.AmountCents, sellerView.Payout.Currency, sellerView.Payout.Approximate));
        Assert.Contains("You'll receive $118.80", sellerView.Payout.Label);
        Assert.Null(await Transfer(order));

        // Picked up: completes by itself in 7 days unless the buyer confirms first.
        var picked = await Read<OrderDto>(await Move(order, "PickedUp", "seller.po1", "seller"));
        Assert.Equal(Now.AddDays(7), picked.AutoCompleteAt!.Value, TimeSpan.FromSeconds(5));
        Assert.Contains("or automatically on", picked.Payout!.Label);

        var done = await Read<OrderDto>(await Move(order, "Completed", "buyer.po1", "buyer"));
        Assert.Equal("Completed", done.Status);
        Assert.Null(done.AutoCompleteAt);

        // $120 − 1 % = $118.80, funded by the buyer's charge, in the order's transfer group.
        var call = fixture.Provider.Transfers.Single(t => t.Request.TransferGroup == $"order_{order}");
        Assert.Equal((11880L, "USD", "acct_US_seller.po1", $"ch_{pi}"), (call.Request.Amount, call.Request.Currency, call.Request.DestinationAccountId, call.Request.SourceChargeId));
        var transfer = (await Transfer(order))!;
        Assert.Equal((TransferStatus.Transferred, call.Id, 11880L, 11880L, "USD"),
            (transfer.Status, transfer.ProviderTransferId, transfer.Amount, transfer.ArrivedAmount!.Value, transfer.ArrivedCurrency));
        var paidView = await OrderAs(order, "seller.po1", "seller");
        Assert.Equal(("Transferred", "Paid out $118.80"), (paidView.Payout!.Status, paidView.Payout.Label));

        // Completing again / the job: no second transfer.
        await Move(order, "Completed", "buyer.po1", "buyer", HttpStatusCode.BadRequest);
        await fixture.RunJobsAsync();
        Assert.Single(fixture.Provider.Transfers, t => t.Request.TransferGroup == $"order_{order}");
    }

    [Fact]
    public async Task Receipts_BuyerSeesWhatTheyPaid_SellerSeesThePayoutStatement()
    {
        var (order, pi) = await PaidOrder("seller.po2", "buyer.po2", 340);

        var buyer = await Read<ReceiptDto>(await Call(s => s.Get.Url($"/api/payments/orders/{order}/receipt"), "buyer.po2"));
        Assert.Equal(("buyer", 34000L, 0L, 34000L, "USD"), (buyer.Role, buyer.ItemAmount, buyer.ShippingAmount, buyer.TotalAmount, buyer.Currency));
        Assert.Equal(($"https://pay.stripe.test/receipts/{pi}", "visa •••• 4242"), (buyer.ReceiptUrl, buyer.Card));
        Assert.Null(buyer.CommissionAmount);
        Assert.Null(buyer.Payout);
        Assert.Equal($"https://pay.stripe.test/receipts/{pi}", (await OrderAs(order, "buyer.po2")).ReceiptUrl);

        var seller = await Read<ReceiptDto>(await Call(s => s.Get.Url($"/api/payments/orders/{order}/receipt"), "seller.po2", role: "seller"));
        Assert.Equal(("seller", 340L, 33660L), (seller.Role, seller.CommissionAmount!.Value, seller.Payout!.Amount));
        Assert.Null(seller.ReceiptUrl);
        Assert.Null(seller.Card);

        await Call(s => s.Get.Url($"/api/payments/orders/{order}/receipt"), "someone.else", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task IrishSeller_EurOrder_SentInUsdAtTheChargesRate_ShownAsAboutEuros()
    {
        var (order, _) = await PaidOrder("seller.po3", "buyer.po3", 95, "IE");

        // €95 − 1 % = €94.05: before the payout it can only be "about" (Stripe converts the USD transfer).
        var before = await OrderAs(order, "seller.po3", "seller");
        Assert.Equal((9405L, "EUR", true), (before.Payout!.AmountCents, before.Payout.Currency, before.Payout.Approximate));
        Assert.Contains("about €94.05", before.Payout.Label);

        await Move(order, "PickedUp", "seller.po3", "seller");
        await Move(order, "Completed", "buyer.po3", "buyer");

        // The charge became USD at 1.12: the share is sent in USD at that rate (rounded down) …
        var call = fixture.Provider.Transfers.Single(t => t.Request.TransferGroup == $"order_{order}");
        Assert.Equal((10533L, "USD", "acct_IE_seller.po3"), (call.Request.Amount, call.Request.Currency, call.Request.DestinationAccountId));
        var payment = await PaymentOf(order);
        Assert.Equal((10640L, "USD", 1.12m), (payment.BalanceAmount!.Value, payment.BalanceCurrency, payment.ExchangeRate!.Value));
        // … and arrives in EUR: what arrived is shown, with the USD amount sent.
        var transfer = (await Transfer(order))!;
        Assert.Equal((9405L, "EUR", 9404L, "EUR"), (transfer.SellerAmount, transfer.SellerCurrency, transfer.ArrivedAmount!.Value, transfer.ArrivedCurrency));
        var after = await OrderAs(order, "seller.po3", "seller");
        Assert.Equal(("Paid out €94.04 (sent as $105.33)", false), (after.Payout!.Label, after.Payout.Approximate));
    }

    // ── auto-complete ────────────────────────────────────────────────────

    [Fact]
    public async Task SevenDaysAfterPickup_TheOrderCompletesItself_AndTheSellerIsPaid()
    {
        var (order, _) = await PaidOrder("seller.po4", "buyer.po4", 200);
        await Move(order, "PickedUp", "seller.po4", "seller");

        fixture.Clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromMinutes(5));
        await fixture.RunJobsAsync();
        Assert.Equal(OrderStatus.PickedUp, (await OrderRow(order)).Status);
        Assert.Null(await Transfer(order));

        fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        await fixture.RunJobsAsync();
        var completed = await Db(db => db.Orders.Include(o => o.Events).AsNoTracking().SingleAsync(o => o.Id == order));
        Assert.Equal(OrderStatus.Completed, completed.Status);
        Assert.Contains(completed.Events, e => e.Status == OrderStatus.Completed && e.By == "system" && e.Note!.Contains("automatically 7 days"));
        Assert.Equal((TransferStatus.Transferred, 19800L), ((await Transfer(order))!.Status, (await Transfer(order))!.Amount));
    }

    [Fact]
    public async Task NotHandedOver_NeverAutoCompletes_OnlyTheBuyerCanComplete()
    {
        var (order, _) = await PaidOrder("seller.po5", "buyer.po5", 60);
        fixture.Clock.Advance(TimeSpan.FromDays(30));
        await fixture.RunJobsAsync();
        Assert.Equal(OrderStatus.SoldAwaitingHandover, (await OrderRow(order)).Status);
        Assert.Null(await Transfer(order));
    }

    // ── seller not ready / provider errors ──────────────────────────────

    [Fact]
    public async Task SellerNotActive_PayoutWaits_ThenGoesWhenTheAccountIsActive_ProviderErrorsAreRetried()
    {
        var (order, _) = await PaidOrder("seller.po6", "buyer.po6", 100, sellerActive: false);
        await Move(order, "Completed", "buyer.po6", "buyer");
        var waiting = (await Transfer(order))!;
        Assert.Equal(TransferStatus.Pending, waiting.Status);
        Assert.StartsWith("Waiting for the seller's payment setup", waiting.FailureMessage);
        Assert.Equal("WaitingForSetup", (await OrderAs(order, "seller.po6", "seller")).Payout!.Status);

        // The account becomes Active, but Stripe fails once: still Pending, retried with a back-off.
        await Db(async db =>
        {
            var account = await db.SellerPaymentAccounts.SingleAsync(a => a.Seller == "seller.po6");
            account.Status = SellerPaymentStatus.Active;
            return await db.SaveChangesAsync();
        });
        fixture.Provider.TransferError = "Insufficient funds in Stripe account";
        try
        {
            await fixture.RunJobsAsync();
        }
        finally
        {
            fixture.Provider.TransferError = null;
        }
        var failed = (await Transfer(order))!;
        Assert.Equal((TransferStatus.Pending, 1), (failed.Status, failed.Attempts));
        Assert.Contains("Insufficient funds", failed.FailureMessage);

        await fixture.RunJobsAsync();
        Assert.Equal(TransferStatus.Pending, (await Transfer(order))!.Status); // too soon for the retry
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        await fixture.RunJobsAsync();
        Assert.Equal((TransferStatus.Transferred, 9900L), ((await Transfer(order))!.Status, (await Transfer(order))!.Amount));
    }

    // ── holds ────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuyerReportsAProblem_NoAutoCompleteNoPayout_UntilSupportReleases()
    {
        var (order, _) = await PaidOrder("seller.po7", "buyer.po7", 150);
        await Move(order, "PickedUp", "seller.po7", "seller");
        Assert.True((await OrderAs(order, "buyer.po7")).CanReportProblem);

        await Call(s => s.Post.Json(new { text = "Wrong part" }).ToUrl($"/api/payments/orders/{order}/problem"), "seller.po7", HttpStatusCode.Forbidden, "seller");
        var held = await Read<OrderDto>(await Call(s => s.Post.Json(new { text = "Wrong part" }).ToUrl($"/api/payments/orders/{order}/problem"), "buyer.po7"));
        Assert.True(held.OnHold);
        Assert.Equal("Buyer reported a problem: Wrong part", held.HoldReason);
        Assert.False(held.CanReportProblem);

        fixture.Clock.Advance(TimeSpan.FromDays(8));
        await fixture.RunJobsAsync();
        Assert.Equal(OrderStatus.PickedUp, (await OrderRow(order)).Status);
        Assert.Contains("on hold", (await OrderAs(order, "seller.po7", "seller")).Payout!.Label);

        // Only an admin releases; complete = true also completes it and pays the seller.
        await Call(s => s.Post.Json(new { complete = true }).ToUrl($"/api/payments/admin/orders/{order}/release"), "buyer.po7", HttpStatusCode.Forbidden);
        var released = await Read<OrderDto>(await Call(s => s.Post.Json(new { complete = true }).ToUrl($"/api/payments/admin/orders/{order}/release"), "admin.po", role: "admin"));
        Assert.False(released.OnHold);
        Assert.Equal("Completed", released.Status);
        Assert.Equal(TransferStatus.Transferred, (await Transfer(order))!.Status);
    }

    [Fact]
    public async Task BuyerConfirmingEndsTheirOwnProblem_AndPaysTheSeller()
    {
        var (order, _) = await PaidOrder("seller.po8", "buyer.po8", 90);
        await Call(s => s.Post.Json(new { text = "Late" }).ToUrl($"/api/payments/orders/{order}/problem"), "buyer.po8");
        await Move(order, "Completed", "buyer.po8", "buyer");
        Assert.Null((await OrderRow(order)).OnHoldSince);
        Assert.Equal(TransferStatus.Transferred, (await Transfer(order))!.Status);
    }

    [Fact]
    public async Task AdminHold_FreezesThePayout_OfACompletedOrderStillWaiting()
    {
        var (order, _) = await PaidOrder("seller.po9", "buyer.po9", 70, sellerActive: false);
        await Move(order, "Completed", "buyer.po9", "buyer");
        var held = await Read<OrderDto>(await Call(s => s.Post.Json(new { text = "Checking the seller" }).ToUrl($"/api/payments/admin/orders/{order}/hold"), "admin.po", role: "admin"));
        Assert.Equal("On hold by support: Checking the seller", held.HoldReason);
        Assert.Equal(TransferStatus.Frozen, (await Transfer(order))!.Status);

        await Db(async db =>
        {
            (await db.SellerPaymentAccounts.SingleAsync(a => a.Seller == "seller.po9")).Status = SellerPaymentStatus.Active;
            return await db.SaveChangesAsync();
        });
        fixture.Clock.Advance(TimeSpan.FromHours(2));
        await fixture.RunJobsAsync();
        Assert.Equal(TransferStatus.Frozen, (await Transfer(order))!.Status);

        await Call(s => s.Post.Json(new { complete = false }).ToUrl($"/api/payments/admin/orders/{order}/release"), "admin.po", role: "admin");
        Assert.Equal(TransferStatus.Transferred, (await Transfer(order))!.Status);
    }

    // ── refunds ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SellerCancelsBeforeHandover_FullRefund_OrderCancelled_NoPayout()
    {
        var (order, pi) = await PaidOrder("seller.po10", "buyer.po10", 220);
        Assert.True((await OrderAs(order, "seller.po10", "seller")).CanCancelAndRefund);
        await Call(s => s.Post.Json(new { text = "Broken" }).ToUrl($"/api/payments/orders/{order}/cancel-and-refund"), "buyer.po10", HttpStatusCode.Forbidden);

        var cancelled = await Read<OrderDto>(await Call(s => s.Post.Json(new { text = "Broken in storage" }).ToUrl($"/api/payments/orders/{order}/cancel-and-refund"), "seller.po10", role: "seller"));
        Assert.Equal(("Cancelled", "Refunded", 22000L), (cancelled.Status, cancelled.PaymentStatus, cancelled.RefundedCents));
        Assert.Contains("Cancelled by the seller: Broken in storage", cancelled.CancelReason);
        var refund = fixture.Provider.Refunds.Single(r => r.PaymentIntentId == pi);
        Assert.Null(refund.Amount); // the whole payment
        Assert.Equal((PaymentStatus.Refunded, 22000L), ((await PaymentOf(order)).Status, (await PaymentOf(order)).RefundedAmount));
        Assert.Equal("Cancelled", (await OrderAs(order, "seller.po10", "seller")).Payout!.Status);

        // Nothing left to refund; a second cancel is refused.
        await Call(s => s.Post.Json(new { text = "again" }).ToUrl($"/api/payments/orders/{order}/cancel-and-refund"), "seller.po10", HttpStatusCode.Conflict, "seller");
    }

    [Fact]
    public async Task SellerCanTCancelAfterPickup()
    {
        var (order, _) = await PaidOrder("seller.po11", "buyer.po11", 50);
        await Move(order, "PickedUp", "seller.po11", "seller");
        Assert.False((await OrderAs(order, "seller.po11", "seller")).CanCancelAndRefund);
        await Call(s => s.Post.Json(new { text = "x" }).ToUrl($"/api/payments/orders/{order}/cancel-and-refund"), "seller.po11", HttpStatusCode.Conflict, "seller");
    }

    [Fact]
    public async Task AdminPartialRefund_AfterThePayout_TakesTheSellersPartBack_ThenTheRest()
    {
        var (order, pi) = await PaidOrder("seller.po12", "buyer.po12", 120);
        await Move(order, "Completed", "buyer.po12", "buyer");
        var tr = (await Transfer(order))!;
        Assert.Equal(11880L, tr.Amount);

        await Call(s => s.Post.Json(new { amount = 2000, reason = "Scratched" }).ToUrl($"/api/payments/admin/orders/{order}/refund"), "seller.po12", HttpStatusCode.Forbidden, "seller");
        var partly = await Read<OrderDto>(await Call(s => s.Post.Json(new { amount = 2000, reason = "Scratched" }).ToUrl($"/api/payments/admin/orders/{order}/refund"), "admin.po", role: "admin"));
        Assert.Equal(("Completed", 2000L), (partly.Status, partly.RefundedCents));
        Assert.Equal(2000L, fixture.Provider.Refunds.Single(r => r.PaymentIntentId == pi).Amount);
        // The seller's share went from $118.80 to $98.80: $20 taken back from the payout.
        var reversal = fixture.Provider.Reversals.Single(r => r.TransferId == tr.ProviderTransferId);
        Assert.Equal(2000L, reversal.Amount);
        Assert.Equal((PaymentStatus.PartiallyRefunded, 2000L), ((await PaymentOf(order)).Status, (await PaymentOf(order)).RefundedAmount));

        // Too much → refused; the rest → the whole remaining payout is taken back.
        await Call(s => s.Post.Json(new { amount = 20000, reason = "x" }).ToUrl($"/api/payments/admin/orders/{order}/refund"), "admin.po", HttpStatusCode.BadRequest, "admin");
        await Call(s => s.Post.Json(new { reason = "Returned" }).ToUrl($"/api/payments/admin/orders/{order}/refund"), "admin.po", role: "admin");
        var all = await Transfer(order);
        Assert.Equal((11880L, TransferStatus.Reversed), (all!.ReversedAmount, all.Status));
        Assert.Equal(OrderPaymentStatus.Refunded, (await OrderRow(order)).PaymentStatus);
    }

    [Fact]
    public async Task OurOwnRefund_ItsEarlyWebhookIsNotASecondRefund_TheSellerIsReversedOnce()
    {
        var (order, pi) = await PaidOrder("seller.po17", "buyer.po17", 120);
        await Move(order, "Completed", "buyer.po17", "buyer");
        var tr = (await Transfer(order))!;

        // Stripe's charge.refunded arrives while our refund call is still running (seen live).
        string? early = null;
        fixture.Provider.WhileRefunding = async (intent, refunded) =>
            early = await fixture.WebhookAsync(fixture.ChargeRefunded($"evt_{Id()}", intent, refunded));
        try
        {
            await Call(s => s.Post.Json(new { amount = 2000, reason = "Scratched" }).ToUrl($"/api/payments/admin/orders/{order}/refund"), "admin.po", role: "admin");
        }
        finally
        {
            fixture.Provider.WhileRefunding = null;
        }

        Assert.Contains("already recorded", early);
        Assert.Single(fixture.Provider.Reversals, r => r.TransferId == tr.ProviderTransferId);
        Assert.Equal(2000L, (await Transfer(order))!.ReversedAmount);
        Assert.Single(await Db(db => db.PaymentRefunds.Where(r => r.OrderId == order).ToListAsync()));
        Assert.Equal(2000L, (await PaymentOf(order)).RefundedAmount);
        Assert.Equal("Paid out $118.80 – $20.00 taken back", (await OrderAs(order, "seller.po17", "seller")).Payout!.Label);
    }

    [Fact]
    public async Task DashboardRefund_FromTheWebhook_LowersThePayout()
    {
        var (order, pi) = await PaidOrder("seller.po13", "buyer.po13", 100);
        var payload = fixture.ChargeRefunded($"evt_{Id()}", pi, 3000);
        Assert.Contains("dashboard refund recorded", await fixture.WebhookAsync(payload));
        Assert.Contains("duplicate", await fixture.WebhookAsync(payload));
        Assert.Equal((PaymentStatus.PartiallyRefunded, 3000L), ((await PaymentOf(order)).Status, (await PaymentOf(order)).RefundedAmount));
        Assert.Equal("Refunded in the Stripe Dashboard", (await Db(db => db.PaymentRefunds.SingleAsync(r => r.OrderId == order))).Reason);

        await Move(order, "Completed", "buyer.po13", "buyer");
        // $100 − $30 refunded − $1 commission.
        Assert.Equal(6900L, (await Transfer(order))!.Amount);
    }

    // ── disputes ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispute_FreezesThePayout_WonReleasesIt()
    {
        var (order, pi) = await PaidOrder("seller.po14", "buyer.po14", 80);
        Assert.Contains("payout on hold", await fixture.WebhookAsync(PaymentsFixture.DisputeEvent($"evt_{Id()}", "charge.dispute.created", pi, "needs_response", 8000)));
        var row = await OrderRow(order);
        Assert.Equal(SellerPayoutService.DisputeHold, row.HoldReason);
        Assert.Equal((PaymentStatus.Disputed, "needs_response"), ((await PaymentOf(order)).Status, (await PaymentOf(order)).DisputeStatus));

        await Move(order, "Completed", "buyer.po14", "buyer");
        Assert.Equal(TransferStatus.Frozen, (await Transfer(order))!.Status);

        Assert.Contains("dispute won", await fixture.WebhookAsync(PaymentsFixture.DisputeEvent($"evt_{Id()}", "charge.dispute.closed", pi, "won", 8000)));
        Assert.Null((await OrderRow(order)).OnHoldSince);
        Assert.Equal((TransferStatus.Transferred, 7920L), ((await Transfer(order))!.Status, (await Transfer(order))!.Amount));
    }

    [Fact]
    public async Task Dispute_ArrivingBeforeTheSuccessWebhook_TheNewOrderStartsOnHold()
    {
        // Seen live with the dispute test card: charge.dispute.created came before payment_intent.succeeded.
        var listing = await Listing("seller.po18", 60);
        var session = await Read<CheckoutSessionDto>(await Call(s => s.Post.Json(new { listingId = listing, quantity = 1, delivery = new { method = "Pickup" } })
            .ToUrl("/api/payments/checkout/buy-now"), "buyer.po18"));
        var pi = fixture.Provider.Intents.Single(i => i.Value.Request.PaymentId == session.PaymentId).Key;

        Assert.Contains("payout on hold", await fixture.WebhookAsync(PaymentsFixture.DisputeEvent($"evt_{Id()}", "charge.dispute.created", pi, "needs_response", 6000)));
        Assert.Contains("already disputed, on hold", await fixture.WebhookAsync(fixture.PaymentEvent($"evt_{Id()}", "payment_intent.succeeded", pi)));

        var order = await OrderRow(session.OrderId);
        Assert.Equal((OrderPaymentStatus.Paid, SellerPayoutService.DisputeHold), (order.PaymentStatus, order.HoldReason));
        Assert.Equal(PaymentStatus.Disputed, (await PaymentOf(order.Id)).Status);
        await Move(order.Id, "Completed", "buyer.po18", "buyer");
        Assert.Equal(TransferStatus.Frozen, (await Transfer(order.Id))!.Status);

        // A repeated success event changes nothing.
        Assert.Contains("already handled", await fixture.WebhookAsync(fixture.PaymentEvent($"evt_{Id()}", "payment_intent.succeeded", pi)));
    }

    [Fact]
    public async Task Dispute_Lost_NoPayout_UnknownChargesAreIgnored()
    {
        var (order, pi) = await PaidOrder("seller.po15", "buyer.po15", 80);
        await fixture.WebhookAsync(PaymentsFixture.DisputeEvent($"evt_{Id()}", "charge.dispute.created", pi, "needs_response", 8000));
        await Move(order, "Completed", "buyer.po15", "buyer");
        Assert.Contains("payout cancelled", await fixture.WebhookAsync(PaymentsFixture.DisputeEvent($"evt_{Id()}", "charge.dispute.closed", pi, "lost", 8000)));
        Assert.Equal(TransferStatus.Cancelled, (await Transfer(order))!.Status);
        Assert.Equal((OrderPaymentStatus.Refunded, SellerPayoutService.DisputeLost), ((await OrderRow(order)).PaymentStatus, (await OrderRow(order)).HoldReason));
        Assert.DoesNotContain(fixture.Provider.Transfers, t => t.Request.TransferGroup == $"order_{order}");

        // Another project's / Stripe's sample charge: ignored.
        Assert.Contains("ignored", await fixture.WebhookAsync(PaymentsFixture.DisputeEvent($"evt_{Id()}", "charge.dispute.created", "pi_not_ours", "needs_response", 500)));
    }

    // ── admin ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_ListsPayments_ShowsAnOrdersDetail_RetriesAPayout_OthersCant()
    {
        var (order, pi) = await PaidOrder("seller.po16", "buyer.po16", 75, sellerActive: false);
        await Move(order, "Completed", "buyer.po16", "buyer");

        await Call(s => s.Get.Url("/api/payments/admin/payments"), "seller.po16", HttpStatusCode.Forbidden, "seller");
        var list = await Read<List<AdminPaymentDto>>(await Call(s => s.Get.Url("/api/payments/admin/payments?user=seller.po16"), "admin.po", role: "admin"));
        var row = Assert.Single(list);
        Assert.Equal((order, "Succeeded", "Pending", 7500L), (row.OrderId, row.Status, row.TransferStatus, row.TotalAmount));

        var detail = await Read<AdminOrderPaymentDto>(await Call(s => s.Get.Url($"/api/payments/admin/orders/{order}"), "admin.po", role: "admin"));
        Assert.Equal((pi, $"ch_{pi}"), (detail.ProviderPaymentId, detail.ProviderChargeId));

        await Db(async db =>
        {
            (await db.SellerPaymentAccounts.SingleAsync(a => a.Seller == "seller.po16")).Status = SellerPaymentStatus.Active;
            return await db.SaveChangesAsync();
        });
        await Call(s => s.Post.Url($"/api/payments/admin/transfers/{row.TransferId}/retry"), "admin.po", role: "admin");
        Assert.Equal(TransferStatus.Transferred, (await Transfer(order))!.Status);

        // A sent payout can be taken back by an admin (e.g. after a lost dispute).
        await Call(s => s.Post.Json(new { amount = 1000, reason = "Lost dispute" }).ToUrl($"/api/payments/admin/transfers/{row.TransferId}/reverse"), "admin.po", role: "admin");
        Assert.Equal(1000L, (await Transfer(order))!.ReversedAmount);
        await Call(s => s.Post.Url($"/api/payments/admin/transfers/{row.TransferId}/retry"), "admin.po", HttpStatusCode.Conflict, "admin");
    }
}
