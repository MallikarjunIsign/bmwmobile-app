using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Alba;
using AuctionService.Controllers;
using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AuctionService.Tests;

/// <summary>Seller status rules and the amounts (cents; 1 % of the item; shipping to the seller).</summary>
public class PaymentRulesTests
{
    private static SellerAccountSnapshot Account(bool payouts = false, bool transfers = false, bool cardRequested = false, bool charges = false,
        bool submitted = false, string[]? due = null, string? disabled = null) =>
        new("acct_1", "US", charges, payouts, transfers, cardRequested, submitted, due ?? [], disabled, true);

    [Fact]
    public void Status_NotStarted_Pending_ActionNeeded_Active()
    {
        Assert.Equal(SellerPaymentStatus.NotStarted, SellerPaymentService.StatusOf(Account()));
        Assert.Equal(SellerPaymentStatus.Pending, SellerPaymentService.StatusOf(Account(submitted: true, disabled: "requirements.pending_verification")));
        Assert.Equal(SellerPaymentStatus.ActionNeeded, SellerPaymentService.StatusOf(Account(submitted: true, due: ["individual.verification.document"])));
        Assert.Equal(SellerPaymentStatus.ActionNeeded, SellerPaymentService.StatusOf(Account(submitted: true, disabled: "rejected.fraud")));
        // US seller (card payments requested): payouts + transfers + charges.
        Assert.Equal(SellerPaymentStatus.Active, SellerPaymentService.StatusOf(Account(true, true, cardRequested: true, charges: true, submitted: true)));
        Assert.NotEqual(SellerPaymentStatus.Active, SellerPaymentService.StatusOf(Account(true, true, cardRequested: true, charges: false, submitted: true)));
        // Irish seller (cross-border, transfers only): charges_enabled isn't needed.
        Assert.Equal(SellerPaymentStatus.Active, SellerPaymentService.StatusOf(Account(true, true, submitted: true)));
    }

    [Theory]
    // Item $340 + shipping $20 = $360; 1 % of $340 = $3.40; the seller receives $356.60.
    [InlineData(34000, 2000, 1.0, 36000, 340, 35660)]
    // Pickup: no shipping.
    [InlineData(34000, 0, 1.0, 34000, 340, 33660)]
    // EUR, half-cent rounds away from zero: 1 % of €12.50 = 12.5 c → 13 c.
    [InlineData(1250, 600, 1.0, 1850, 13, 1837)]
    // The percentage is a setting.
    [InlineData(10000, 500, 2.5, 10500, 250, 10250)]
    public void Amounts(long item, long shipping, double percent, long total, long commission, long seller)
    {
        var b = PaymentAmounts.For(item, shipping, (decimal)percent);
        Assert.Equal((total, commission, seller), (b.Total, b.Commission, b.SellerAmount));
    }

    [Fact]
    public void WholeUnits_ToCents() => Assert.Equal(34000L, PaymentAmounts.ToMinor(340));
}

/// <summary>The real Stripe webhook signature check (no network): valid, wrong secret, too old, not this app's object.</summary>
public class StripeWebhookSignatureTests
{
    private static StripePaymentProvider Provider(string? secret = PaymentsFixture.WebhookSecret) => new(
        Options.Create(new StripeOptions { WebhookSecret = secret }), Options.Create(new PaymentOptions()),
        NullLogger<StripePaymentProvider>.Instance);

    [Fact]
    public void ValidSignature_ReadsTheEvent()
    {
        var payload = PaymentsFixture.AccountUpdated("evt_1", "acct_1", "IE", active: true);
        var evt = Provider().ParseWebhook(payload, PaymentsFixture.Sign(payload));

        Assert.Equal(("evt_1", "account.updated", true), (evt.Id, evt.Type, evt.OwnedByApp));
        Assert.True(evt.Account!.PayoutsEnabled && evt.Account.TransfersActive);
        Assert.False(evt.Account.CardPaymentsRequested);
    }

    [Fact]
    public void WrongSecret_OldTimestamp_MissingHeader_OrNoSecret_AreRejected()
    {
        var payload = PaymentsFixture.AccountUpdated("evt_2", "acct_1", "US", active: true);
        Assert.Throws<WebhookSignatureException>(() => Provider().ParseWebhook(payload, PaymentsFixture.Sign(payload, "whsec_other")));
        Assert.Throws<WebhookSignatureException>(() => Provider().ParseWebhook(payload, PaymentsFixture.Sign(payload, at: DateTimeOffset.UtcNow.AddMinutes(-10))));
        Assert.Throws<WebhookSignatureException>(() => Provider().ParseWebhook(payload, null));
        Assert.Throws<WebhookSignatureException>(() => Provider(secret: null).ParseWebhook(payload, PaymentsFixture.Sign(payload)));
        // A changed body no longer matches its signature.
        Assert.Throws<WebhookSignatureException>(() => Provider().ParseWebhook(payload.Replace("acct_1", "acct_2"), PaymentsFixture.Sign(payload)));
    }

    [Fact]
    public void ObjectsOfTheOtherProject_AreNotOurs()
    {
        var untagged = PaymentsFixture.AccountUpdated("evt_3", "acct_9", "US", active: true, app: null);
        Assert.False(Provider().ParseWebhook(untagged, PaymentsFixture.Sign(untagged)).OwnedByApp);
        var other = PaymentsFixture.AccountUpdated("evt_4", "acct_9", "US", active: true, app: "other-project");
        Assert.False(Provider().ParseWebhook(other, PaymentsFixture.Sign(other)).OwnedByApp);
    }
}

/// <summary>Sellers' payment accounts end to end: create, onboarding link, webhook → Active, publishing rule.</summary>
[Collection("payments")]
public class SellerPaymentApiTests(PaymentsFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<IScenarioResult> Call(Action<Scenario> configure, string user, string? country, HttpStatusCode status = HttpStatusCode.OK,
        string role = "seller")
    {
        return await fixture.Host.Scenario(s =>
        {
            s.RemoveClaim("role");
            s.WithClaim(new Claim("role", role));
            s.WithClaim(new Claim("username", user));
            s.WithClaim(new Claim("email", $"{user}@example.com"));
            if (country is not null) s.WithClaim(new Claim("account_country", country));
            configure(s);
            s.StatusCodeShouldBe(status);
        });
    }

    private async Task<T> Read<T>(IScenarioResult r) => JsonSerializer.Deserialize<T>(await r.ReadAsTextAsync(), Json)!;

    private Task<IScenarioResult> Webhook(string payload, string? signature = null, HttpStatusCode status = HttpStatusCode.OK) =>
        fixture.Host.Scenario(s =>
        {
            s.Post.Text(payload).ToUrl("/api/payments/stripe/webhook");
            s.WithRequestHeader("Stripe-Signature", signature ?? PaymentsFixture.Sign(payload));
            s.StatusCodeShouldBe(status);
        });

    private async Task<SellerPaymentAccount> Stored(string seller)
    {
        using var scope = fixture.Host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AuctionDbContext>().SellerPaymentAccounts.AsNoTracking().SingleAsync(a => a.Seller == seller);
    }

    [Fact]
    public async Task UsSeller_FirstVisit_Onboarding_WebhookMakesItActive_ThenPublishing()
    {
        const string seller = "us.seller1";
        // No account yet: "Not started".
        var before = await Read<SellerPaymentAccountDto>(await Call(s => s.Get.Url("/api/payments/seller-account"), seller, "US"));
        Assert.Equal(("NotStarted", true, false), (before.Status, before.Available, before.CanPublish));

        // First visit creates it (US, tagged at the provider).
        var created = await Read<SellerPaymentAccountDto>(await Call(s => s.Post.Json(new { }).ToUrl("/api/payments/seller-account"), seller, "US"));
        Assert.Equal("NotStarted", created.Status);
        Assert.Contains(fixture.Provider.Created, a => a.Seller == seller && a.Country == "US" && a.Email == $"{seller}@example.com");

        // The hosted onboarding link returns to the Gateway's page (then the app).
        var link = await Read<PaymentLinkDto>(await Call(s => s.Post.Json(new { }).ToUrl("/api/payments/seller-account/onboarding-link"), seller, "US"));
        Assert.Contains("connect.stripe.test/setup/", link.Url);
        Assert.Contains(Uri.EscapeDataString("/payments/stripe/onboarding-return"), link.Url);

        // Can't publish a US listing yet.
        var draft = await UsDraft(seller);
        var refused = await Call(s => s.Post.Json(new { }).ToUrl($"/api/listings/{draft}/publish"), seller, "US", HttpStatusCode.Conflict);
        Assert.Contains("PAYMENTS_NOT_READY", await refused.ReadAsTextAsync());

        // Stripe: onboarding finished → account.updated → Active.
        var account = await Stored(seller);
        var payload = PaymentsFixture.AccountUpdated("evt_active_1", account.ProviderAccountId, "US", active: true);
        await Webhook(payload);
        Assert.Equal(SellerPaymentStatus.Active, (await Stored(seller)).Status);

        var now = await Read<SellerPaymentAccountDto>(await Call(s => s.Get.Url("/api/payments/seller-account"), seller, "US"));
        Assert.Equal(("Active", true), (now.Status, now.CanPublish));
        await Call(s => s.Post.Json(new { }).ToUrl($"/api/listings/{draft}/publish"), seller, "US");

        // The dashboard link (payouts) works once onboarding was submitted.
        var dashboard = await Read<PaymentLinkDto>(await Call(s => s.Post.Json(new { }).ToUrl("/api/payments/seller-account/dashboard-link"), seller, "US"));
        Assert.Contains("/express/", dashboard.Url);
    }

    [Fact]
    public async Task Webhooks_AreHandledOnce_OthersObjectsIgnored_BadSignaturesRejected()
    {
        const string seller = "ie.seller1";
        await Call(s => s.Post.Json(new { }).ToUrl("/api/payments/seller-account"), seller, "IE");
        var account = await Stored(seller);
        Assert.Equal("IE", account.Country);

        // Pending verification first …
        var pending = PaymentsFixture.AccountUpdated("evt_ie_1", account.ProviderAccountId, "IE", active: false);
        Assert.Contains("seller account synced", await (await Webhook(pending)).ReadAsTextAsync());
        Assert.Equal(SellerPaymentStatus.Pending, (await Stored(seller)).Status);
        // … the same event again: nothing changes.
        Assert.Contains("duplicate", await (await Webhook(pending)).ReadAsTextAsync());

        // Irish seller: active with transfers + payouts (no card payments – cross-border).
        await Webhook(PaymentsFixture.AccountUpdated("evt_ie_2", account.ProviderAccountId, "IE", active: true));
        Assert.Equal(SellerPaymentStatus.Active, (await Stored(seller)).Status);

        // An event about the other project's account (no app tag): ignored, not stored.
        var other = PaymentsFixture.AccountUpdated("evt_other_1", account.ProviderAccountId, "IE", active: false, app: null);
        Assert.Contains("ignored", await (await Webhook(other)).ReadAsTextAsync());
        Assert.Equal(SellerPaymentStatus.Active, (await Stored(seller)).Status);

        // Wrong signature: 400, nothing changes.
        await Webhook(PaymentsFixture.AccountUpdated("evt_ie_3", account.ProviderAccountId, "IE", active: false),
            PaymentsFixture.Sign("something else"), HttpStatusCode.BadRequest);
        Assert.Equal(SellerPaymentStatus.Active, (await Stored(seller)).Status);

        using var scope = fixture.Host.Services.CreateScope();
        var events = await scope.ServiceProvider.GetRequiredService<AuctionDbContext>().ProcessedWebhookEvents.AsNoTracking()
            .Where(e => e.EventId.StartsWith("evt_ie_") || e.EventId.StartsWith("evt_other_")).Select(e => e.EventId).ToListAsync();
        Assert.Equal(["evt_ie_1", "evt_ie_2"], events.Order());
    }

    [Fact]
    public async Task IndiaAndNigeria_ComingSoon_CurrentFlowUnchanged()
    {
        foreach (var (seller, country) in new[] { ("in.seller1", "IN"), ("ng.seller1", "NG") })
        {
            var dto = await Read<SellerPaymentAccountDto>(await Call(s => s.Post.Json(new { }).ToUrl("/api/payments/seller-account"), seller, country));
            Assert.Equal(("NotAvailable", false, true), (dto.Status, dto.Available, dto.CanPublish));
            Assert.Equal("Online payment is coming soon in your country.", dto.Message);
            Assert.DoesNotContain(fixture.Provider.Created, a => a.Seller == seller);
            var link = await Call(s => s.Post.Json(new { }).ToUrl("/api/payments/seller-account/onboarding-link"), seller, country, HttpStatusCode.BadRequest);
            Assert.Contains("PAYMENTS_NOT_AVAILABLE", await link.ReadAsTextAsync());
        }
    }

    [Fact]
    public async Task AdminCreatesASeller_IdentityOpensTheAccount_OnlyOnce()
    {
        // What IdentityService calls (internal, not routed by the Gateway).
        for (var i = 0; i < 2; i++)
            await fixture.Host.Scenario(s =>
            {
                s.Post.Json(new NewSellerPaymentAccountRequest("new.us.seller", "US", "new@example.com")).ToUrl("/api/internal/payment-accounts");
                s.StatusCodeShouldBe(HttpStatusCode.OK);
            });
        Assert.Single(fixture.Provider.Created, a => a.Seller == "new.us.seller");

        await fixture.Host.Scenario(s =>
        {
            s.Post.Json(new NewSellerPaymentAccountRequest("new.in.seller", "IN", null)).ToUrl("/api/internal/payment-accounts");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        Assert.DoesNotContain(fixture.Provider.Created, a => a.Seller == "new.in.seller");
    }

    [Fact]
    public async Task SetUpPayments_ReusesTheSellersExistingStripeAccount_NoDuplicate()
    {
        const string seller = "reuse.seller1";
        // Finished on the other database: an older unfinished account and the active one exist at Stripe.
        fixture.Provider.AddExistingAccount(seller, "US", active: false, detailsSubmitted: false, created: DateTime.UtcNow.AddDays(-2));
        var active = fixture.Provider.AddExistingAccount(seller, "US", active: true, created: DateTime.UtcNow.AddDays(-1));

        // Profile → Payments shows Active straight away (linked, not created).
        var shown = await Read<SellerPaymentAccountDto>(await Call(s => s.Get.Url("/api/payments/seller-account"), seller, "US"));
        Assert.Equal(("Active", true), (shown.Status, shown.CanPublish));
        Assert.Equal(active.AccountId, (await Stored(seller)).ProviderAccountId);

        // "Set up payments" / the onboarding link use it too: never a new Stripe account.
        await Call(s => s.Post.Json(new { }).ToUrl("/api/payments/seller-account"), seller, "US");
        await Call(s => s.Post.Json(new { }).ToUrl("/api/payments/seller-account/onboarding-link"), seller, "US");
        Assert.DoesNotContain(fixture.Provider.Created, a => a.Seller == seller);
    }

    [Fact]
    public async Task SetUpPayments_WithoutAnAccountHere_LinksTheUnfinishedOne_InsteadOfCreating()
    {
        const string seller = "reuse.seller2";
        var started = fixture.Provider.AddExistingAccount(seller, "US", active: false, detailsSubmitted: false);
        var dto = await Read<SellerPaymentAccountDto>(await Call(s => s.Post.Json(new { }).ToUrl("/api/payments/seller-account"), seller, "US"));
        Assert.Equal("NotStarted", dto.Status);
        Assert.Equal(started.AccountId, (await Stored(seller)).ProviderAccountId);
        Assert.DoesNotContain(fixture.Provider.Created, a => a.Seller == seller);
    }

    [Fact]
    public async Task AdminSync_LinksAndRelinksEverySeller_RefreshesStatus_CreatesNothing()
    {
        // A: this database stores an unfinished account; the seller finished another one on the other database.
        await Call(s => s.Post.Json(new { }).ToUrl("/api/payments/seller-account"), "sync.a", "US");
        var oldA = (await Stored("sync.a")).ProviderAccountId;
        var activeA = fixture.Provider.AddExistingAccount("sync.a", "US", active: true);
        // B: only at Stripe (onboarded on the other database). C: another project's account (no app tag) – never linked.
        var activeB = fixture.Provider.AddExistingAccount("sync.b", "IE", active: true);
        fixture.Provider.AddExistingAccount("sync.c", "US", active: true, ownedByApp: false);
        var createdBefore = fixture.Provider.Created.Count;

        // Only admins.
        await Call(s => s.Post.Json(new { }).ToUrl("/api/payments/admin/seller-accounts/sync"), "sync.a", "US", HttpStatusCode.Forbidden);

        var results = await Read<List<SellerPaymentSyncResult>>(await Call(s => s.Post.Json(new { }).ToUrl("/api/payments/admin/seller-accounts/sync"),
            "admin.user", "US", role: "admin"));
        var a = Assert.Single(results, r => r.Seller == "sync.a");
        Assert.Equal((activeA.AccountId, "Active", 2), (a.AccountId, a.Status, a.AccountsAtProvider));
        Assert.StartsWith($"relinked (was {oldA})", a.Action);
        var b = Assert.Single(results, r => r.Seller == "sync.b");
        Assert.Equal((activeB.AccountId, "Active", "linked"), (b.AccountId, b.Status, b.Action));
        Assert.DoesNotContain(results, r => r.Seller == "sync.c");

        Assert.Equal((activeA.AccountId, SellerPaymentStatus.Active), ((await Stored("sync.a")).ProviderAccountId, (await Stored("sync.a")).Status));
        Assert.Equal(("IE", SellerPaymentStatus.Active), ((await Stored("sync.b")).Country, (await Stored("sync.b")).Status));
        Assert.Equal(createdBefore, fixture.Provider.Created.Count);

        // Running it again changes nothing.
        var again = await Read<List<SellerPaymentSyncResult>>(await Call(s => s.Post.Json(new { }).ToUrl("/api/payments/admin/seller-accounts/sync"),
            "admin.user", "US", role: "admin"));
        Assert.Equal("refreshed", Assert.Single(again, r => r.Seller == "sync.a").Action);
    }

    [Fact]
    public async Task Buyers_CantSeePaymentSetup()
    {
        await Call(s => s.Get.Url("/api/payments/seller-account"), "a.buyer", "US", HttpStatusCode.Forbidden, role: "buyer");
    }

    [Fact]
    public async Task ReturnPages_OpenTheApp()
    {
        var page = await fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/payments/stripe/onboarding-return");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        Assert.Contains("shopcarparts://payments", await page.ReadAsTextAsync());
    }

    /// <summary>A complete US draft (ready to publish) of <paramref name="seller"/>.</summary>
    private async Task<string> UsDraft(string seller)
    {
        int partTypeId;
        using (var scope = fixture.Host.Services.CreateScope())
            partTypeId = (await scope.ServiceProvider.GetRequiredService<AuctionDbContext>().PartTypes.FirstAsync(p => p.Name == "Dash cam")).Id;

        var draft = await Read<AuctionDto>(await Call(s => s.Post.Json(new CreateListingDto
        {
            Kind = "CommonPart", PartTypeId = partTypeId, CountryCode = "US", LocationName = "Houston, TX", Latitude = 29.7604, Longitude = -95.3698,
        }).ToUrl("/api/listings"), seller, "US", HttpStatusCode.Created));

        await Call(s => s.Put.Json(new SaveListingDto
        {
            SaleType = "FixedPrice",
            PartDetails = new PartDetailsDto { BrandType = "Aftermarket", Brand = "Garmin", Condition = "New", Quantity = 2 },
            PartPrice = 120,
            DeliveryOptions = ["Pickup", "Shipping"],
        }).ToUrl($"/api/listings/{draft.Id}"), seller, "US");

        var form = new MultipartFormDataContent();
        for (var i = 0; i < 3; i++)
        {
            var content = new ByteArrayContent(TestImages.Jpeg());
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(content, "files", $"{i}.jpg");
        }
        await Call(s => s.Post.MultipartFormData(form).ToUrl($"/api/auctions/{draft.Id}/images"), seller, "US");
        return draft.Id;
    }
}
