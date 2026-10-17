using System.Net;
using System.Security.Cryptography;
using System.Text;
using Alba;
using Alba.Security;
using AuctionService.Payments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Wolverine;

namespace AuctionService.Tests;

/// <summary>
/// AuctionService with online payments ON, against a fake provider (no network, no keys): accounts and payments are kept
/// in memory, and webhooks are checked with the REAL Stripe signature code (StripePaymentProvider.ParseWebhook) and a
/// test secret. The clock can be moved (reservations, deadlines); the payment jobs are run by hand; the bid history is fake.
/// </summary>
public class PaymentsFixture : IAsyncLifetime
{
    public const string WebhookSecret = "whsec_test_shopcarparts_secret";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("auctions").WithUsername("postgres").WithPassword("postgrespw").Build();

    public IAlbaHost Host { get; private set; } = null!;
    public FakePaymentProvider Provider { get; private set; } = null!;
    public MovableClock Clock { get; } = new();
    public FakeBidHistory Bids { get; } = new();
    public string UploadsPath { get; } = Path.Combine(Path.GetTempPath(), $"payments-test-uploads-{Guid.NewGuid():N}");

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
            builder.UseSetting("ImageStorage:LocalPath", UploadsPath);
            builder.UseSetting("Stripe:WebhookSecret", WebhookSecret);
            builder.UseSetting("Payments:RunJobs", "false");
            // The test-only setting is on; it applies only while the provider is in test mode (Provider.IsTestMode).
            builder.UseSetting("Payments:AllowInactiveSellersInTestMode", "true");
            builder.ConfigureServices(services =>
            {
                services.RunWolverineInSoloMode();
                services.DisableAllExternalWolverineTransports();
                services.RemoveAll<IPaymentProvider>();
                services.AddSingleton<FakePaymentProvider>();
                services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<FakePaymentProvider>());
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.RemoveAll<IBidHistory>();
                services.AddSingleton<IBidHistory>(Bids);
            });
        }, new JwtSecurityStub().With(new System.Security.Claims.Claim("role", "seller")));
        Provider = Host.Services.GetRequiredService<FakePaymentProvider>();
    }

    public async Task DisposeAsync()
    {
        await Host.StopAsync();
        await Host.DisposeAsync();
        await _postgres.DisposeAsync();
        if (Directory.Exists(UploadsPath)) Directory.Delete(UploadsPath, recursive: true);
    }

    /// <summary>The payment jobs, as PaymentJobs runs them every 30 seconds (deadlines, then auto-complete and payouts).</summary>
    public async Task RunJobsAsync()
    {
        await using (var scope = Host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<PaymentDeadlineService>().RunAsync(CancellationToken.None);
        await using (var scope = Host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<SellerPayoutService>().RunAsync(CancellationToken.None);
    }

    /// <summary>Posts a signed webhook like Stripe; returns the response body.</summary>
    public async Task<string> WebhookAsync(string payload, string? signature = null, HttpStatusCode status = HttpStatusCode.OK)
    {
        var result = await Host.Scenario(s =>
        {
            s.Post.Text(payload).ToUrl("/api/payments/stripe/webhook");
            s.WithRequestHeader("Stripe-Signature", signature ?? Sign(payload));
            s.StatusCodeShouldBe(status);
        });
        return await result.ReadAsTextAsync();
    }

    /// <summary>A Stripe-Signature header for the payload (what Stripe / the Stripe CLI sends).</summary>
    public static string Sign(string payload, string secret = WebhookSecret, DateTimeOffset? at = null)
    {
        var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();
        return $"t={timestamp},v1={signature}";
    }

    /// <summary>An account.updated event as Stripe sends it.</summary>
    public static string AccountUpdated(string eventId, string accountId, string country, bool active, string? app = "shopcarparts",
        bool detailsSubmitted = true, string[]? currentlyDue = null)
    {
        var metadata = app is null ? "{}" : $"{{\"app\":\"{app}\"}}";
        var due = string.Join(",", (currentlyDue ?? []).Select(d => $"\"{d}\""));
        var cardPayments = country == "US" ? $",\"card_payments\":\"{(active ? "active" : "inactive")}\"" : "";
        var account = $$"""
            {"id":"{{accountId}}","object":"account","country":"{{country}}","type":"express",
             "charges_enabled":{{(active && country == "US" ? "true" : "false")}},"payouts_enabled":{{(active ? "true" : "false")}},
             "details_submitted":{{(detailsSubmitted ? "true" : "false")}},
             "capabilities":{"transfers":"{{(active ? "active" : "inactive")}}"{{cardPayments}}},
             "requirements":{"currently_due":[{{due}}],"past_due":[],"disabled_reason":null},
             "metadata":{{metadata}}}
            """;
        return Event(eventId, "account.updated", account);
    }

    /// <summary>A payment_intent.* event (succeeded / payment_failed / canceled) for a payment the fake provider made.</summary>
    public string PaymentEvent(string eventId, string type, string paymentIntentId, string? failure = null, long? amount = null, string? app = "shopcarparts")
    {
        var pi = Provider.Intents[paymentIntentId];
        var status = type switch { "payment_intent.succeeded" => "succeeded", "payment_intent.canceled" => "canceled", _ => "requires_payment_method" };
        var error = failure is null ? "null" : $"{{\"type\":\"card_error\",\"code\":\"card_declined\",\"message\":\"{failure}\"}}";
        var metadata = app is null ? "{}" : $"{{\"app\":\"{app}\",\"payment_id\":\"{pi.Request.PaymentId}\"}}";
        var obj = $$"""
            {"id":"{{paymentIntentId}}","object":"payment_intent","amount":{{amount ?? pi.Request.Amount}},"currency":"{{pi.Request.Currency.ToLowerInvariant()}}",
             "status":"{{status}}","latest_charge":"ch_{{paymentIntentId}}","last_payment_error":{{error}},"metadata":{{metadata}}}
            """;
        return Event(eventId, type, obj);
    }

    /// <summary>charge.refunded (e.g. a refund made in the Stripe Dashboard): the charge with its total refunded amount.</summary>
    public string ChargeRefunded(string eventId, string paymentIntentId, long amountRefunded)
    {
        var pi = Provider.Intents[paymentIntentId];
        var obj = $$$"""
            {"id":"ch_{{{paymentIntentId}}}","object":"charge","amount":{{{pi.Request.Amount}}},"amount_refunded":{{{amountRefunded}}},
             "currency":"{{{pi.Request.Currency.ToLowerInvariant()}}}","payment_intent":"{{{paymentIntentId}}}","refunded":{{{(amountRefunded >= pi.Request.Amount ? "true" : "false")}}},
             "metadata":{"app":"shopcarparts","payment_id":"{{{pi.Request.PaymentId}}}"}}
            """;
        return Event(eventId, "charge.refunded", obj);
    }

    /// <summary>charge.dispute.created / closed (disputes carry no metadata).</summary>
    public static string DisputeEvent(string eventId, string type, string paymentIntentId, string status, long amount, string currency = "usd") =>
        Event(eventId, type, $$$"""
            {"id":"dp_{{{paymentIntentId}}}","object":"dispute","amount":{{{amount}}},"currency":"{{{currency}}}","charge":"ch_{{{paymentIntentId}}}",
             "payment_intent":"{{{paymentIntentId}}}","reason":"fraudulent","status":"{{{status}}}","metadata":{}}
            """);

    private static string Event(string eventId, string type, string obj) =>
        "{\"id\":\"" + eventId + "\",\"object\":\"event\",\"api_version\":\"2024-06-20\",\"created\":1760000000,\"livemode\":false," +
        "\"type\":\"" + type + "\",\"data\":{\"object\":" + obj + "}}";
}

[CollectionDefinition("payments")]
public class PaymentsCollection : ICollectionFixture<PaymentsFixture>;

/// <summary>A clock the tests move forward.</summary>
public class MovableClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

public class FakeBidHistory : IBidHistory
{
    public Dictionary<string, List<BidRecord>> Bids { get; } = new();

    public Task<IReadOnlyList<BidRecord>> GetBidsAsync(string auctionId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BidRecord>>(Bids.GetValueOrDefault(auctionId) ?? []);
}

/// <summary>Seller accounts and payments in memory; webhooks parsed by the real Stripe code.</summary>
public class FakePaymentProvider(IOptions<StripeOptions> stripe, IOptions<PaymentOptions> payments) : IPaymentProvider
{
    private readonly StripePaymentProvider _stripe = new(
        Options.Create(new StripeOptions { WebhookSecret = stripe.Value.WebhookSecret }), payments,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<StripePaymentProvider>.Instance);

    public record Intent(PaymentIntentRequest Request, string ClientSecret);

    public Dictionary<string, SellerAccountSnapshot> Accounts { get; } = new();
    public List<NewSellerAccount> Created { get; } = [];
    public Dictionary<string, Intent> Intents { get; } = new();
    public HashSet<string> Cancelled { get; } = [];
    public List<string> Refunded { get; } = [];
    public List<string> Customers { get; } = [];

    public string Name => "stripe";
    public bool IsConfigured => true;
    /// <summary>Like Stripe test keys: only then does Payments:AllowInactiveSellersInTestMode apply.</summary>
    public bool IsTestMode { get; set; }

    public Task<SellerAccountSnapshot> CreateSellerAccountAsync(NewSellerAccount account, CancellationToken ct)
    {
        lock (Accounts)
        {
            var id = $"acct_test_{Accounts.Count + 1}_{account.Seller}";
            var snapshot = new SellerAccountSnapshot(id, account.Country, false, false, false, account.Country == "US", false, [], null, true,
                account.Seller, DateTime.UtcNow);
            Accounts[id] = snapshot;
            Created.Add(account);
            return Task.FromResult(snapshot);
        }
    }

    public Task<SellerAccountSnapshot> GetSellerAccountAsync(string accountId, CancellationToken ct)
    {
        // Like Stripe for an id it doesn't know ("No such account").
        lock (Accounts)
            return Accounts.TryGetValue(accountId, out var account)
                ? Task.FromResult(account)
                : throw new PaymentProviderException($"No such account: '{accountId}'");
    }

    /// <summary>Like Stripe's account list filtered by our tags (app + seller); accounts of other projects aren't returned.</summary>
    public Task<IReadOnlyList<SellerAccountSnapshot>> FindSellerAccountsAsync(string? seller, CancellationToken ct)
    {
        lock (Accounts)
            return Task.FromResult<IReadOnlyList<SellerAccountSnapshot>>(Accounts.Values
                .Where(a => a.OwnedByApp && a.Seller is not null && (seller is null || a.Seller == seller)).ToList());
    }

    /// <summary>An account that already exists at Stripe (e.g. made while the other database was in use).</summary>
    public SellerAccountSnapshot AddExistingAccount(string seller, string country, bool active, bool detailsSubmitted = true,
        DateTime? created = null, bool ownedByApp = true)
    {
        lock (Accounts)
        {
            var id = $"acct_existing_{Accounts.Count + 1}_{seller}";
            var snapshot = new SellerAccountSnapshot(id, country, active && country == "US", active, active, country == "US", detailsSubmitted, [],
                null, ownedByApp, seller, created ?? DateTime.UtcNow);
            Accounts[id] = snapshot;
            return snapshot;
        }
    }

    public Task<string> CreateOnboardingLinkAsync(string accountId, string returnUrl, string refreshUrl, CancellationToken ct) =>
        Task.FromResult($"https://connect.stripe.test/setup/{accountId}?return={Uri.EscapeDataString(returnUrl)}");

    public Task<string> CreateDashboardLinkAsync(string accountId, CancellationToken ct) =>
        Task.FromResult($"https://connect.stripe.test/express/{accountId}");

    public Task<string> CreateCustomerAsync(string buyer, string? email, CancellationToken ct)
    {
        lock (Customers) Customers.Add(buyer);
        return Task.FromResult($"cus_test_{buyer}");
    }

    public Task<string> CreateCustomerSessionKeyAsync(string customerId, CancellationToken ct) => Task.FromResult($"ek_test_{customerId}");

    public Task<NewPaymentIntent> CreatePaymentIntentAsync(PaymentIntentRequest request, CancellationToken ct)
    {
        var id = $"pi_test_{request.PaymentId[..8]}";
        lock (Intents) Intents[id] = new Intent(request, $"{id}_secret_x");
        return Task.FromResult(new NewPaymentIntent(id, $"{id}_secret_x"));
    }

    public Task CancelPaymentIntentAsync(string paymentIntentId, CancellationToken ct)
    {
        lock (Cancelled) Cancelled.Add(paymentIntentId);
        return Task.CompletedTask;
    }

    public record RefundCall(string PaymentIntentId, long? Amount, string IdempotencyKey);
    public record TransferCall(TransferRequest Request, string Id);
    public record ReversalCall(string TransferId, long Amount, string IdempotencyKey);

    public List<RefundCall> Refunds { get; } = [];
    public List<TransferCall> Transfers { get; } = [];
    public List<ReversalCall> Reversals { get; } = [];
    /// <summary>How a EUR charge is converted into the platform's USD balance (Stripe's rate in the tests).</summary>
    public decimal EurToUsd { get; set; } = 1.12m;
    /// <summary>Make the next transfers fail (like a Stripe error) until set back.</summary>
    public string? TransferError { get; set; }

    /// <summary>
    /// Runs while Stripe is refunding (before RefundAsync returns): lets a test deliver the refund's own charge.refunded
    /// webhook early, as Stripe can.
    /// </summary>
    public Func<string, long, Task>? WhileRefunding { get; set; }

    public async Task<string> RefundAsync(string paymentIntentId, long? amount, string reason, string idempotencyKey, CancellationToken ct)
    {
        lock (Refunded)
        {
            // Like Stripe's idempotency: the same key refunds once.
            if (Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey) is { } done) return $"re_{done.IdempotencyKey}";
            Refunded.Add(paymentIntentId);
            Refunds.Add(new RefundCall(paymentIntentId, amount, idempotencyKey));
        }
        if (WhileRefunding is { } hook)
        {
            long total;
            lock (Intents) total = Intents.TryGetValue(paymentIntentId, out var i) ? i.Request.Amount : 0;
            var refunded = Refunds.Where(r => r.PaymentIntentId == paymentIntentId).Sum(r => r.Amount ?? total);
            await hook(paymentIntentId, refunded);
        }
        return $"re_{idempotencyKey}";
    }

    public Task<ChargeSnapshot> GetChargeAsync(string chargeId, CancellationToken ct)
    {
        var pi = chargeId.StartsWith("ch_") ? chargeId[3..] : chargeId;
        Intent intent;
        lock (Intents) intent = Intents[pi];
        var eur = intent.Request.Currency.Equals("EUR", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(new ChargeSnapshot(chargeId, pi, intent.Request.Amount, intent.Request.Currency.ToUpperInvariant(), 0,
            $"https://pay.stripe.test/receipts/{pi}", "visa •••• 4242",
            eur ? (long)Math.Round(intent.Request.Amount * EurToUsd) : intent.Request.Amount, "USD", eur ? EurToUsd : null));
    }

    public Task<TransferResult> CreateTransferAsync(TransferRequest request, CancellationToken ct)
    {
        if (TransferError is { } error) throw new PaymentProviderException(error);
        lock (Transfers)
        {
            if (Transfers.FirstOrDefault(t => t.Request.TransferId == request.TransferId) is { } done)
                return Task.FromResult(new TransferResult(done.Id, null, null));
            var id = $"tr_test_{Transfers.Count + 1}";
            Transfers.Add(new TransferCall(request, id));
            // An Irish account receives the USD transfer in EUR.
            var irish = request.DestinationAccountId.Contains("_IE_", StringComparison.Ordinal);
            return Task.FromResult(irish
                ? new TransferResult(id, (long)Math.Round(request.Amount / EurToUsd), "EUR")
                : new TransferResult(id, request.Amount, request.Currency));
        }
    }

    public Task<string> ReverseTransferAsync(string transferId, long amount, string reason, string idempotencyKey, CancellationToken ct)
    {
        lock (Reversals) Reversals.Add(new ReversalCall(transferId, amount, idempotencyKey));
        return Task.FromResult($"trr_{idempotencyKey}");
    }

    public PaymentWebhookEvent ParseWebhook(string payload, string? signatureHeader)
    {
        var evt = _stripe.ParseWebhook(payload, signatureHeader);
        // What Stripe would now return for the account.
        if (evt.Account is { } account)
            lock (Accounts)
                Accounts[account.AccountId] = account with
                {
                    Seller = account.Seller ?? Accounts.GetValueOrDefault(account.AccountId)?.Seller,
                    Created = Accounts.GetValueOrDefault(account.AccountId)?.Created ?? account.Created,
                };
        return evt;
    }
}
