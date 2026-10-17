using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Alba;
using AuctionService.Controllers;
using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using AuctionService.Payments;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Tracking;

namespace AuctionService.Tests;

/// <summary>
/// Buyer payments (Step 2), against the fake provider: Buy now with the 15-minute reservation, declined cards, the
/// auction's payment deadline with reminders, second-chance offers, Unsold, and non-payment strikes. An order is paid
/// only by the signed webhook.
/// </summary>
[Collection("payments")]
public class BuyerPaymentTests(PaymentsFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const double HoustonLat = 29.7604, HoustonLon = -95.3698;

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

    /// <summary>A live US listing (Houston) of <paramref name="seller"/>, with an Active payment account.</summary>
    private async Task<string> UsListing(string seller, SaleType sale, int price, int quantity = 1, bool sellerActive = true,
        DeliveryOptions delivery = DeliveryOptions.Pickup | DeliveryOptions.Shipping)
    {
        var id = Id();
        await Db(async db =>
        {
            db.Auctions.Add(new Auction
            {
                Id = id, Seller = seller, Status = Status.Live, SaleType = sale, StartingPrice = price, ReservePrice = price,
                AuctionEnd = sale == SaleType.FixedPrice ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddDays(1),
                Item = new Item
                {
                    Id = id, Kind = ListingKind.CommonPart, ItemType = ItemType.Part, Make = "", Model = "", Description = "Test part",
                    Title = $"Dash cam {id[..4]}", CountryCode = "US", Currency = "USD", LocationName = "Houston, TX",
                    Latitude = HoustonLat, Longitude = HoustonLon, DeliveryOptions = delivery, Quantity = quantity,
                },
            });
            if (sellerActive && !await db.SellerPaymentAccounts.AnyAsync(a => a.Seller == seller))
                db.SellerPaymentAccounts.Add(new SellerPaymentAccount
                {
                    Seller = seller, ProviderAccountId = $"acct_{seller}", Country = "US", Status = SellerPaymentStatus.Active,
                    PayoutsEnabled = true, ChargesEnabled = true, TransfersActive = true, DetailsSubmitted = true,
                });
            await db.SaveChangesAsync();
            return 0;
        });
        return id;
    }

    private Task<IScenarioResult> BuyNow(string buyer, string listing, int quantity = 1, string method = "Pickup", HttpStatusCode status = HttpStatusCode.OK,
        double? lat = null, double? lon = null) =>
        Call(s => s.Post.Json(new
        {
            listingId = listing, quantity,
            delivery = method == "Shipping"
                ? (object)new
                {
                    method, saveAddress = false, latitude = lat, longitude = lon,
                    address = new { name = "Pat Buyer", phone = "+12015550100", line1 = "1 Main St", city = "Dallas", postcode = "75201", countryCode = "US" },
                }
                : new { method },
        }).ToUrl("/api/payments/checkout/buy-now"), buyer, status);

    private async Task<Auction> Listing(string id) => await Db(db => db.Auctions.Include(a => a.Item).AsNoTracking().SingleAsync(a => a.Id == id));

    // ── Buy now ──────────────────────────────────────────────────────────

    [Fact]
    public async Task BuyNow_ReservesTheQuantity_TheWebhookCreatesThePaidOrder_OnlyOnce()
    {
        var listing = await UsListing("seller.bn1", SaleType.FixedPrice, 120, quantity: 2);

        var session = await Read<CheckoutSessionDto>(await BuyNow("buyer.bn1", listing));
        // The amount is the backend's: $120 × 1, pickup = no shipping; USD, cents.
        Assert.Equal((12000L, 0L, 12000L, "USD"), (session.ItemAmount, session.ShippingAmount, session.TotalAmount, session.Currency));
        Assert.NotNull(session.ReservedUntil);
        Assert.Equal(fixture.Clock.GetUtcNow().UtcDateTime.AddMinutes(15), session.ReservedUntil!.Value, TimeSpan.FromSeconds(5));
        Assert.StartsWith("pi_test_", session.ClientSecret);
        Assert.Equal("cus_test_buyer.bn1", session.CustomerId);

        // One unit is held: one left for others; no order yet (no payment yet).
        var held = await Listing(listing);
        Assert.Equal((1, 1, 0), (held.QuantityReserved, held.QuantityAvailable, held.QuantitySold));
        Assert.False(await Db(db => db.Orders.AnyAsync(o => o.AuctionId == listing)));

        // The provider's PaymentIntent: the platform charge, tagged, with the order's transfer group.
        var pi = fixture.Provider.Intents.Single(i => i.Value.Request.PaymentId == session.PaymentId);
        Assert.Equal($"order_{session.OrderId}", pi.Value.Request.TransferGroup);

        // Stripe: payment_intent.succeeded → the order exists, Paid, fulfilment can start.
        var payload = fixture.PaymentEvent($"evt_{Id()}", "payment_intent.succeeded", pi.Key);
        Assert.Contains("buy now paid", await fixture.WebhookAsync(payload));
        var order = await Db(db => db.Orders.AsNoTracking().SingleAsync(o => o.AuctionId == listing));
        Assert.Equal((session.OrderId, OrderStatus.SoldAwaitingHandover, OrderPaymentStatus.Paid, DeliveryOptions.Pickup),
            (order.Id, order.Status, order.PaymentStatus, order.DeliveryMethod));
        var sold = await Listing(listing);
        Assert.Equal((0, 1, 1), (sold.QuantityReserved, sold.QuantitySold, sold.QuantityAvailable));

        // The same event again: nothing changes.
        Assert.Contains("duplicate", await fixture.WebhookAsync(payload));
        Assert.Equal(1, await Db(db => db.Orders.CountAsync(o => o.AuctionId == listing)));
        var status = await Read<PaymentStatusDto>(await Call(s => s.Get.Url($"/api/payments/{session.PaymentId}"), "buyer.bn1"));
        Assert.Equal(("Succeeded", session.OrderId), (status.Status, status.OrderId));
    }

    [Fact]
    public async Task BuyNow_Shipping_TheTotalIncludesTheCalculatedShipping()
    {
        var listing = await UsListing("seller.bn2", SaleType.FixedPrice, 120);
        // To Dallas (~225 mi): USPS price for the dash cam's default package.
        var session = await Read<CheckoutSessionDto>(await BuyNow("buyer.bn2", listing, method: "Shipping", lat: 32.7767, lon: -96.797));
        Assert.True(session.ShippingAmount > 0);
        Assert.Equal(session.ItemAmount + session.ShippingAmount, session.TotalAmount);
        Assert.Equal(0, session.ShippingAmount % 100);

        var pi = fixture.Provider.Intents.Single(i => i.Value.Request.PaymentId == session.PaymentId).Key;
        await fixture.WebhookAsync(fixture.PaymentEvent($"evt_{Id()}", "payment_intent.succeeded", pi));
        var order = await Db(db => db.Orders.AsNoTracking().SingleAsync(o => o.AuctionId == listing));
        Assert.Equal((DeliveryOptions.Shipping, (int)(session.ShippingAmount / 100), "Dallas"), (order.DeliveryMethod!.Value, order.ShippingFee!.Value, order.ShipTo!.City));
    }

    [Fact]
    public async Task BuyNow_DeclinedCard_ThenClosingTheSheet_ReleasesTheReservation_NoOrder()
    {
        var listing = await UsListing("seller.bn3", SaleType.FixedPrice, 80);
        var session = await Read<CheckoutSessionDto>(await BuyNow("buyer.bn3", listing));
        var pi = fixture.Provider.Intents.Single(i => i.Value.Request.PaymentId == session.PaymentId).Key;

        // 4000 0000 0000 9995: declined (insufficient funds). The buyer may still try another card.
        Assert.Contains("payment failed", await fixture.WebhookAsync(
            fixture.PaymentEvent($"evt_{Id()}", "payment_intent.payment_failed", pi, failure: "Your card has insufficient funds.")));
        var failed = await Read<PaymentStatusDto>(await Call(s => s.Get.Url($"/api/payments/{session.PaymentId}"), "buyer.bn3"));
        Assert.Equal(("Failed", "Your card has insufficient funds."), (failed.Status, failed.FailureMessage));
        Assert.Equal(1, (await Listing(listing)).QuantityReserved);

        // The buyer closes the sheet: released at once, the payment is cancelled at the provider, no order.
        await Call(s => s.Post.Json(new { }).ToUrl($"/api/payments/{session.PaymentId}/cancel"), "buyer.bn3", HttpStatusCode.NoContent);
        Assert.Equal((0, 1), ((await Listing(listing)).QuantityReserved, (await Listing(listing)).QuantityAvailable));
        Assert.Contains(pi, fixture.Provider.Cancelled);
        Assert.False(await Db(db => db.Orders.AnyAsync(o => o.AuctionId == listing)));
    }

    [Fact]
    public async Task BuyNow_LastUnitReserved_OthersSeeReserved_After15MinutesItIsFreeAgain_ALatePaymentIsRefunded()
    {
        var listing = await UsListing("seller.bn4", SaleType.FixedPrice, 60);
        var first = await Read<CheckoutSessionDto>(await BuyNow("buyer.bn4a", listing));

        var refused = await BuyNow("buyer.bn4b", listing, status: HttpStatusCode.Conflict);
        Assert.Contains("RESERVED", await refused.ReadAsTextAsync());

        // 15 minutes pass (the jobs run every 30 s): released, the first payment is cancelled.
        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        await fixture.RunJobsAsync();
        var firstPi = fixture.Provider.Intents.Single(i => i.Value.Request.PaymentId == first.PaymentId).Key;
        Assert.Contains(firstPi, fixture.Provider.Cancelled);
        Assert.Equal(1, (await Listing(listing)).QuantityAvailable);

        // The second buyer buys it.
        var second = await Read<CheckoutSessionDto>(await BuyNow("buyer.bn4b", listing));
        var secondPi = fixture.Provider.Intents.Single(i => i.Value.Request.PaymentId == second.PaymentId).Key;
        await fixture.WebhookAsync(fixture.PaymentEvent($"evt_{Id()}", "payment_intent.succeeded", secondPi));
        Assert.Equal(Status.Sold, (await Listing(listing)).Status);

        // The first buyer's payment arrives anyway (it was already confirmed): refunded, no second order.
        Assert.Contains("refunded", await fixture.WebhookAsync(fixture.PaymentEvent($"evt_{Id()}", "payment_intent.succeeded", firstPi)));
        Assert.Contains(firstPi, fixture.Provider.Refunded);
        Assert.Equal(1, await Db(db => db.Orders.CountAsync(o => o.AuctionId == listing)));
    }

    [Fact]
    public async Task BuyNow_TheAmountComesOnlyFromTheBackend_AMismatchingWebhookIsNotApplied()
    {
        var listing = await UsListing("seller.bn5", SaleType.FixedPrice, 50);
        var session = await Read<CheckoutSessionDto>(await BuyNow("buyer.bn5", listing));
        var pi = fixture.Provider.Intents.Single(i => i.Value.Request.PaymentId == session.PaymentId).Key;
        Assert.Contains("amount mismatch", await fixture.WebhookAsync(fixture.PaymentEvent($"evt_{Id()}", "payment_intent.succeeded", pi, amount: 100)));
        Assert.False(await Db(db => db.Orders.AnyAsync(o => o.AuctionId == listing)));
    }

    [Fact]
    public async Task SellerNotActive_BuyersCantPay_UnlessTheTestOnlySettingAppliesInTestMode()
    {
        var listing = await UsListing("seller.inactive", SaleType.FixedPrice, 70, sellerActive: false);
        fixture.Provider.IsTestMode = false;
        // The setting is on in this fixture, but not with live keys.
        var refused = await BuyNow("buyer.in1", listing, status: HttpStatusCode.Conflict);
        Assert.Contains("Seller is finishing payment setup", await refused.ReadAsTextAsync());

        fixture.Provider.IsTestMode = true;
        try
        {
            await BuyNow("buyer.in1", listing);
        }
        finally
        {
            fixture.Provider.IsTestMode = false;
        }
    }

    [Fact]
    public async Task OldAppsBuyNow_IsRefusedInTheUsa_PaymentRequired()
    {
        var listing = await UsListing("seller.old", SaleType.FixedPrice, 40);
        var refused = await Call(s => s.Post.Json(new BuyListingDto { Quantity = 1 }).ToUrl($"/api/listings/{listing}/buy"), "buyer.old",
            HttpStatusCode.Conflict);
        Assert.Contains("PAYMENT_REQUIRED", await refused.ReadAsTextAsync());
    }

    // ── Auctions: deadline, reminders, second chance, strikes ───────────

    private async Task<Order> Win(string listing, string seller, string winner, int amount)
    {
        await fixture.Host.InvokeMessageAndWaitAsync(new AuctionFinished
        {
            AuctionId = listing, Seller = seller, Winner = winner, Amount = amount, ItemSold = true, Status = "Sold", EndReason = "TimeEnded",
        });
        return await Db(db => db.Orders.AsNoTracking().SingleAsync(o => o.AuctionId == listing && o.Buyer == winner));
    }

    [Fact]
    public async Task AuctionWin_WaitsForPayment48Hours_DeliveryFirst_ThenPaidByTheWebhook()
    {
        var listing = await UsListing("seller.au1", SaleType.Auction, 300, delivery: DeliveryOptions.Pickup);
        var order = await Win(listing, "seller.au1", "winner.au1", 340);
        Assert.Equal((OrderStatus.AwaitingPayment, OrderPaymentStatus.AwaitingPayment), (order.Status, order.PaymentStatus));
        Assert.Equal(fixture.Clock.GetUtcNow().UtcDateTime.AddHours(48), order.PaymentDueAt!.Value, TimeSpan.FromSeconds(5));

        // The seller can't start before the payment; the buyer sees "Pay now".
        var forSeller = await Read<OrderDto>(await Call(s => s.Get.Url($"/api/orders/{order.Id}"), "seller.au1", role: "seller"));
        Assert.StartsWith("Waiting for payment – 4", forSeller.PaymentLabel);
        Assert.Empty(forSeller.Actions);
        await Call(s => s.Post.Json(new { status = "ReadyForPickup" }).ToUrl($"/api/orders/{order.Id}/status"), "seller.au1",
            HttpStatusCode.BadRequest, role: "seller");
        var forBuyer = await Read<OrderDto>(await Call(s => s.Get.Url($"/api/orders/{order.Id}"), "winner.au1"));
        Assert.True(forBuyer.CanPay);

        // Pickup only: chosen already. $340 (part price $300 + winning bid $40), no shipping.
        var session = await Read<CheckoutSessionDto>(await Call(s => s.Post.Json(new { }).ToUrl($"/api/payments/orders/{order.Id}/pay"), "winner.au1"));
        Assert.Equal((34000L, 0L, 34000L), (session.ItemAmount, session.ShippingAmount, session.TotalAmount));
        await Call(s => s.Post.Json(new { }).ToUrl($"/api/payments/orders/{order.Id}/pay"), "someone.else", HttpStatusCode.Forbidden);

        var pi = fixture.Provider.Intents.Single(i => i.Value.Request.PaymentId == session.PaymentId).Key;
        await fixture.WebhookAsync(fixture.PaymentEvent($"evt_{Id()}", "payment_intent.succeeded", pi));
        var paid = await Db(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id));
        Assert.Equal((OrderStatus.SoldAwaitingHandover, OrderPaymentStatus.Paid), (paid.Status, paid.PaymentStatus));
        // Now the seller can go on.
        await Call(s => s.Post.Json(new { status = "ReadyForPickup" }).ToUrl($"/api/orders/{order.Id}/status"), "seller.au1", role: "seller");
    }

    [Fact]
    public async Task BothDeliveryOptions_ChooseFirst_ShippingIsPartOfTheTotal()
    {
        var listing = await UsListing("seller.au2", SaleType.Auction, 200);
        var order = await Win(listing, "seller.au2", "winner.au2", 220);
        var early = await Call(s => s.Post.Json(new { }).ToUrl($"/api/payments/orders/{order.Id}/pay"), "winner.au2", HttpStatusCode.BadRequest);
        Assert.Contains("DELIVERY_REQUIRED", await early.ReadAsTextAsync());

        await Call(s => s.Post.Json(new ChooseDeliveryDto
        {
            Method = "Shipping", SaveAddress = false, Latitude = 32.7767, Longitude = -96.797,
            Address = new AddressDto { Name = "Pat", Phone = "+12015550100", Line1 = "1 Main St", City = "Dallas", Postcode = "75201", CountryCode = "US" },
        }).ToUrl($"/api/orders/{order.Id}/delivery-method"), "winner.au2");
        var withShipping = await Db(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id));
        var session = await Read<CheckoutSessionDto>(await Call(s => s.Post.Json(new { }).ToUrl($"/api/payments/orders/{order.Id}/pay"), "winner.au2"));
        Assert.Equal((22000L, withShipping.ShippingFee!.Value * 100L), (session.ItemAmount, session.ShippingAmount));
    }

    [Fact]
    public async Task NotPaid_Reminders_Strike_SecondChanceChain_ThenUnsold()
    {
        var listing = await UsListing("seller.sc", SaleType.Auction, 300, delivery: DeliveryOptions.Pickup);
        fixture.Bids.Bids[listing] =
        [
            new("winner.sc", 500, "Accepted"), new("second.sc", 450, "Accepted"), new("third.sc", 400, "Accepted"),
            new("fourth.sc", 350, "Accepted"), new("fifth.sc", 320, "Accepted"), new("seller.sc", 999, "Accepted"),
            new("second.sc", 420, "Accepted"),
        ];
        var won = await Win(listing, "seller.sc", "winner.sc", 500);

        // Half time → the first reminder; 2 hours before → the last one.
        fixture.Clock.Advance(TimeSpan.FromHours(25));
        await fixture.RunJobsAsync();
        Assert.NotNull((await Db(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == won.Id))).HalfwayReminderAt);
        fixture.Clock.Advance(TimeSpan.FromHours(22));
        await fixture.RunJobsAsync();
        Assert.NotNull((await Db(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == won.Id))).FinalReminderAt);

        // 48 hours passed: cancelled "Not paid in time", a strike, offer 1 to the next-highest bidder at THEIR highest bid.
        fixture.Clock.Advance(TimeSpan.FromHours(2));
        await fixture.RunJobsAsync();
        var cancelled = await Db(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == won.Id));
        Assert.Equal((OrderStatus.Cancelled, OrderPaymentStatus.Expired, "Not paid in time"), (cancelled.Status, cancelled.PaymentStatus, cancelled.CancelReason));
        Assert.Equal(1, await Db(db => db.NonPaymentStrikes.CountAsync(s => s.User == "winner.sc")));
        var offer1 = await Db(db => db.SecondChanceOffers.AsNoTracking().SingleAsync(o => o.AuctionId == listing));
        Assert.Equal(("second.sc", 450, 1), (offer1.Bidder, offer1.Amount, offer1.Round));
        var listed = await Read<List<SecondChanceOfferDto>>(await Call(s => s.Get.Url("/api/payments/offers"), "second.sc"));
        Assert.Single(listed, o => o.Id == offer1.Id && o.Amount == 450);

        // The seller's listing shows it.
        var mine = await Read<List<MyListingDto>>(await Call(s => s.Get.Url("/api/listings/mine"), "seller.sc", role: "seller"));
        Assert.Equal("OfferedToNextBidder", mine.Single(l => l.Id == listing).State);

        // Declining: no penalty → offer 2 at once.
        await Call(s => s.Post.Json(new { }).ToUrl($"/api/payments/offers/{offer1.Id}/decline"), "second.sc", HttpStatusCode.NoContent);
        var offer2 = await Db(db => db.SecondChanceOffers.AsNoTracking().SingleAsync(o => o.AuctionId == listing && o.Round == 2));
        Assert.Equal("third.sc", offer2.Bidder);

        // Not answering counts as declining (no strike) → offer 3.
        fixture.Clock.Advance(TimeSpan.FromHours(49));
        await fixture.RunJobsAsync();
        var offer3 = await Db(db => db.SecondChanceOffers.AsNoTracking().SingleAsync(o => o.AuctionId == listing && o.Round == 3));
        Assert.Equal(("fourth.sc", 350), (offer3.Bidder, offer3.Amount));

        // Accepting creates an order waiting for payment until the offer's deadline …
        var accepted = await Read<OrderDto>(await Call(s => s.Post.Json(new { }).ToUrl($"/api/payments/offers/{offer3.Id}/accept"), "fourth.sc"));
        Assert.Equal(("AwaitingPayment", 350), (accepted.Status, accepted.FinalTotal));
        // … not paid either: no strike for a second-chance buyer; 3 offers were made → Unsold.
        fixture.Clock.Advance(TimeSpan.FromHours(49));
        await fixture.RunJobsAsync();
        Assert.Equal(Status.Unsold, (await Listing(listing)).Status);
        Assert.Equal(0, await Db(db => db.NonPaymentStrikes.CountAsync(s => s.User != "winner.sc" && s.AuctionId == listing)));
        Assert.False(await Db(db => db.SecondChanceOffers.AnyAsync(o => o.AuctionId == listing && o.Bidder == "fifth.sc")));
        mine = await Read<List<MyListingDto>>(await Call(s => s.Get.Url("/api/listings/mine"), "seller.sc", role: "seller"));
        Assert.Equal("UnsoldNotPaid", mine.Single(l => l.Id == listing).State);
    }

    [Fact]
    public async Task SecondChanceBuyer_Pays_BecomesTheWinner()
    {
        var listing = await UsListing("seller.sc2", SaleType.Auction, 100, delivery: DeliveryOptions.Pickup);
        fixture.Bids.Bids[listing] = [new("winner.sc2", 150, "Accepted"), new("next.sc2", 140, "Accepted")];
        await Win(listing, "seller.sc2", "winner.sc2", 150);
        fixture.Clock.Advance(TimeSpan.FromHours(49));
        await fixture.RunJobsAsync();

        var offer = await Db(db => db.SecondChanceOffers.AsNoTracking().SingleAsync(o => o.AuctionId == listing));
        var order = await Read<OrderDto>(await Call(s => s.Post.Json(new { }).ToUrl($"/api/payments/offers/{offer.Id}/accept"), "next.sc2"));
        var session = await Read<CheckoutSessionDto>(await Call(s => s.Post.Json(new { }).ToUrl($"/api/payments/orders/{order.Id}/pay"), "next.sc2"));
        Assert.Equal(14000L, session.TotalAmount);
        var pi = fixture.Provider.Intents.Single(i => i.Value.Request.PaymentId == session.PaymentId).Key;
        await fixture.WebhookAsync(fixture.PaymentEvent($"evt_{Id()}", "payment_intent.succeeded", pi));

        var auction = await Listing(listing);
        Assert.Equal((Status.Sold, "next.sc2", 140), (auction.Status, auction.Winner, auction.SoldAmount!.Value));
        Assert.Equal(SecondChanceStatus.Paid, (await Db(db => db.SecondChanceOffers.AsNoTracking().SingleAsync(o => o.Id == offer.Id))).Status);
    }

    [Fact]
    public async Task TwoUnpaidWins_BlockBidding_SkippedForOffers_AdminClears()
    {
        var user = $"striker.{Id()[..6]}";
        for (var i = 0; i < 2; i++)
        {
            var listing = await UsListing($"seller.st{i}", SaleType.Auction, 100, delivery: DeliveryOptions.Pickup);
            await Win(listing, $"seller.st{i}", user, 120);
        }
        fixture.Clock.Advance(TimeSpan.FromHours(49));
        await fixture.RunJobsAsync();

        Task<string?> Blocked() => Db(db => new StrikeService(db, fixture.Host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PaymentOptions>>(),
            fixture.Clock).BiddingBlockedReasonAsync(user, CancellationToken.None));
        Assert.Contains("You can't bid right now: 2 won auctions weren't paid", await Blocked());
        var me = await Call(s => s.Get.Url("/api/payments/strikes/me"), user);
        Assert.Contains("\"strikes\":2", await me.ReadAsTextAsync());

        // A blocked bidder doesn't get second-chance offers.
        var other = await UsListing("seller.st9", SaleType.Auction, 100, delivery: DeliveryOptions.Pickup);
        fixture.Bids.Bids[other] = [new("winner.st9", 130, "Accepted"), new(user, 125, "Accepted"), new("clean.st9", 110, "Accepted")];
        await Win(other, "seller.st9", "winner.st9", 130);
        fixture.Clock.Advance(TimeSpan.FromHours(49));
        await fixture.RunJobsAsync();
        Assert.Equal("clean.st9", (await Db(db => db.SecondChanceOffers.AsNoTracking().SingleAsync(o => o.AuctionId == other))).Bidder);

        // Only an admin clears strikes.
        await Call(s => s.Post.Json(new { }).ToUrl($"/api/payments/admin/strikes/{user}/clear"), "not.admin", HttpStatusCode.Forbidden);
        await Call(s => s.Post.Json(new { }).ToUrl($"/api/payments/admin/strikes/{user}/clear"), "the.admin", role: "admin");
        Assert.Null(await Blocked());
    }

    [Fact]
    public async Task IndiaAndNigeria_AuctionOrdersDontWaitForPayment()
    {
        var id = Id();
        await Db(async db =>
        {
            db.Auctions.Add(new Auction
            {
                Id = id, Seller = "seller.in", Status = Status.Live, StartingPrice = 1000, ReservePrice = 1000, AuctionEnd = DateTime.UtcNow,
                Item = new Item { Id = id, Kind = ListingKind.CommonPart, ItemType = ItemType.Part, Make = "", Model = "", Description = "", CountryCode = "IN",
                    Currency = "INR", DeliveryOptions = DeliveryOptions.Pickup },
            });
            await db.SaveChangesAsync();
            return 0;
        });
        var order = await Win(id, "seller.in", "buyer.in", 1200);
        Assert.Equal((OrderStatus.SoldAwaitingHandover, OrderPaymentStatus.NotRequired, (DateTime?)null), (order.Status, order.PaymentStatus, order.PaymentDueAt));
    }
}
