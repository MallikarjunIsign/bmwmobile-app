using Microsoft.Extensions.Options;
using Stripe;

namespace AuctionService.Payments;

/// <summary>
/// Stripe Connect: Express connected accounts (Stripe-hosted onboarding and dashboard), separate charges and transfers.
/// The keys come only from user-secrets / environment (Stripe:SecretKey, Stripe:WebhookSecret) and are never logged.
/// Every object created here carries metadata app=&lt;Payments:AppTag&gt; (the test account is shared).
/// <para>
/// Capabilities: US sellers request card_payments + transfers; Irish sellers transfers only – the charge is on the US
/// platform and Stripe moves the money to them as a cross-border transfer (US ↔ EEA is supported, without
/// on_behalf_of, full service agreement). So charges_enabled is only expected for US sellers.
/// </para>
/// </summary>
public class StripePaymentProvider(IOptions<StripeOptions> stripe, IOptions<PaymentOptions> payments, ILogger<StripePaymentProvider> logger)
    : IPaymentProvider
{
    private readonly StripeClient? _client = stripe.Value.IsConfigured ? new StripeClient(stripe.Value.SecretKey) : null;
    private readonly string _appTag = payments.Value.AppTag;

    public string Name => "stripe";
    public bool IsConfigured => _client is not null;
    public bool IsTestMode => stripe.Value.IsTestMode;

    private StripeClient Client => _client ?? throw new PaymentProviderException("Online payment isn't set up (Stripe keys missing).");

    /// <summary>
    /// Accounts v2 (new Stripe platforms can't create v1 accounts): Express dashboard, the platform pays Stripe's fees
    /// and carries losses (required with Express; the platform charges the buyer anyway). Stripe requires the merchant
    /// card_payments capability next to the recipient's transfers. What the app already knows is prefilled; Stripe's
    /// hosted onboarding asks for the rest. Everything else (status, onboarding links, transfers) uses v1 – it works
    /// with v2 accounts.
    /// </summary>
    public async Task<SellerAccountSnapshot> CreateSellerAccountAsync(NewSellerAccount account, CancellationToken ct)
    {
        var country = account.Country.ToUpperInvariant();
        var options = new Stripe.V2.Core.AccountCreateOptions
        {
            ContactEmail = string.IsNullOrWhiteSpace(account.Email) ? null : account.Email,
            DisplayName = string.IsNullOrWhiteSpace(account.DisplayName) ? account.Seller : account.DisplayName,
            Dashboard = "express",
            Identity = new Stripe.V2.Core.AccountCreateIdentityOptions { Country = country.ToLowerInvariant(), EntityType = "individual" },
            Defaults = new Stripe.V2.Core.AccountCreateDefaultsOptions
            {
                Currency = country == "US" ? "usd" : "eur",
                Responsibilities = new Stripe.V2.Core.AccountCreateDefaultsResponsibilitiesOptions
                {
                    FeesCollector = "application",
                    LossesCollector = "application",
                },
                Profile = new Stripe.V2.Core.AccountCreateDefaultsProfileOptions
                {
                    ProductDescription = "Sells used and new car parts on the ShopCarParts marketplace",
                },
            },
            Configuration = new Stripe.V2.Core.AccountCreateConfigurationOptions
            {
                Merchant = new Stripe.V2.Core.AccountCreateConfigurationMerchantOptions
                {
                    // Automotive Parts and Accessories Stores.
                    Mcc = "5533",
                    Capabilities = new Stripe.V2.Core.AccountCreateConfigurationMerchantCapabilitiesOptions
                    {
                        CardPayments = new Stripe.V2.Core.AccountCreateConfigurationMerchantCapabilitiesCardPaymentsOptions { Requested = true },
                    },
                },
                Recipient = new Stripe.V2.Core.AccountCreateConfigurationRecipientOptions
                {
                    Capabilities = new Stripe.V2.Core.AccountCreateConfigurationRecipientCapabilitiesOptions
                    {
                        StripeBalance = new Stripe.V2.Core.AccountCreateConfigurationRecipientCapabilitiesStripeBalanceOptions
                        {
                            StripeTransfers = new Stripe.V2.Core.AccountCreateConfigurationRecipientCapabilitiesStripeBalanceStripeTransfersOptions { Requested = true },
                        },
                    },
                },
            },
            Metadata = new Dictionary<string, string> { ["app"] = _appTag, ["seller"] = account.Seller },
        };
        try
        {
            var created = await Client.V2.Core.Accounts.CreateAsync(options, cancellationToken: ct);
            logger.LogInformation("Stripe connected account {AccountId} created for seller {Seller} ({Country})", created.Id, account.Seller, country);
            return await GetSellerAccountAsync(created.Id, ct);
        }
        catch (StripeException e)
        {
            // Stripe's message (never contains the key): e.g. "…signed up for Connect…".
            logger.LogWarning("Stripe refused the connected account for {Seller}: {Code} {Message}", account.Seller,
                e.StripeError?.Code, e.StripeError?.Message);
            throw new PaymentProviderException(e.StripeError?.Message ?? "Couldn't create the payment account", e);
        }
    }

    public async Task<SellerAccountSnapshot> GetSellerAccountAsync(string accountId, CancellationToken ct)
    {
        try
        {
            return ToSnapshot(await Client.V1.Accounts.GetAsync(accountId, cancellationToken: ct));
        }
        catch (StripeException e)
        {
            throw new PaymentProviderException(e.StripeError?.Message ?? "Couldn't read the payment account", e);
        }
    }

    public async Task<IReadOnlyList<SellerAccountSnapshot>> FindSellerAccountsAsync(string? seller, CancellationToken ct)
    {
        try
        {
            // Connected accounts can't be searched by metadata: list them all (a few hundred at most) and filter.
            var found = new List<SellerAccountSnapshot>();
            await foreach (var account in Client.V1.Accounts.ListAutoPagingAsync(new AccountListOptions { Limit = 100 }, cancellationToken: ct))
            {
                var snapshot = ToSnapshot(account);
                if (snapshot.OwnedByApp && snapshot.Seller is { Length: > 0 } && (seller is null || snapshot.Seller == seller))
                    found.Add(snapshot);
            }
            return found;
        }
        catch (StripeException e)
        {
            throw new PaymentProviderException(e.StripeError?.Message ?? "Couldn't read the payment accounts", e);
        }
    }

    public async Task<string> CreateOnboardingLinkAsync(string accountId, string returnUrl, string refreshUrl, CancellationToken ct)
    {
        try
        {
            var link = await Client.V1.AccountLinks.CreateAsync(new AccountLinkCreateOptions
            {
                Account = accountId,
                Type = "account_onboarding",
                ReturnUrl = returnUrl,
                RefreshUrl = refreshUrl,
            }, cancellationToken: ct);
            return link.Url;
        }
        catch (StripeException e)
        {
            throw new PaymentProviderException(e.StripeError?.Message ?? "Couldn't open the payment setup", e);
        }
    }

    public async Task<string> CreateDashboardLinkAsync(string accountId, CancellationToken ct)
    {
        try
        {
            var link = await Client.V1.Accounts.LoginLinks.CreateAsync(accountId, cancellationToken: ct);
            return link.Url;
        }
        catch (StripeException e)
        {
            throw new PaymentProviderException(e.StripeError?.Message ?? "Couldn't open the payouts dashboard", e);
        }
    }

    public async Task<string> CreateCustomerAsync(string buyer, string? email, CancellationToken ct)
    {
        try
        {
            var customer = await Client.V1.Customers.CreateAsync(new CustomerCreateOptions
            {
                Email = string.IsNullOrWhiteSpace(email) ? null : email,
                Metadata = new Dictionary<string, string> { ["app"] = _appTag, ["buyer"] = buyer },
            }, cancellationToken: ct);
            return customer.Id;
        }
        catch (StripeException e)
        {
            throw new PaymentProviderException(e.StripeError?.Message ?? "Couldn't prepare the payment", e);
        }
    }

    public async Task<string> CreateCustomerSessionKeyAsync(string customerId, CancellationToken ct)
    {
        try
        {
            var key = await Client.V1.EphemeralKeys.CreateAsync(new EphemeralKeyCreateOptions
            {
                Customer = customerId,
                StripeVersion = StripeConfiguration.ApiVersion,
            }, cancellationToken: ct);
            return key.Secret;
        }
        catch (StripeException e)
        {
            throw new PaymentProviderException(e.StripeError?.Message ?? "Couldn't prepare the payment", e);
        }
    }

    public async Task<NewPaymentIntent> CreatePaymentIntentAsync(PaymentIntentRequest request, CancellationToken ct)
    {
        var metadata = new Dictionary<string, string>(request.Metadata) { ["app"] = _appTag, ["payment_id"] = request.PaymentId };
        try
        {
            var intent = await Client.V1.PaymentIntents.CreateAsync(new PaymentIntentCreateOptions
            {
                Amount = request.Amount,
                Currency = request.Currency.ToLowerInvariant(),
                Customer = request.CustomerId,
                // Cards (Google Pay is a card wallet) – not the account's other methods (the account is shared).
                AllowedPaymentMethodTypes = ["card"],
                // The charge stays on the platform; the seller's share is a transfer in this group later.
                TransferGroup = request.TransferGroup,
                Description = request.Description,
                ReceiptEmail = request.ReceiptEmail,
                Metadata = metadata,
            }, new RequestOptions { IdempotencyKey = $"pi-{request.PaymentId}" }, ct);
            return new NewPaymentIntent(intent.Id, intent.ClientSecret);
        }
        catch (StripeException e)
        {
            logger.LogWarning("Stripe refused the payment {PaymentId}: {Code} {Message}", request.PaymentId, e.StripeError?.Code, e.StripeError?.Message);
            throw new PaymentProviderException(e.StripeError?.Message ?? "Couldn't start the payment", e);
        }
    }

    public async Task CancelPaymentIntentAsync(string paymentIntentId, CancellationToken ct)
    {
        try
        {
            await Client.V1.PaymentIntents.CancelAsync(paymentIntentId, cancellationToken: ct);
        }
        catch (StripeException e) when (e.StripeError?.Code is "payment_intent_unexpected_state")
        {
            // Already succeeded or cancelled: the webhook decides.
        }
        catch (StripeException e)
        {
            logger.LogWarning("Couldn't cancel payment {PaymentIntentId}: {Message}", paymentIntentId, e.StripeError?.Message);
        }
    }

    public async Task<string> RefundAsync(string paymentIntentId, long? amount, string reason, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            var refund = await Client.V1.Refunds.CreateAsync(new RefundCreateOptions
            {
                PaymentIntent = paymentIntentId,
                Amount = amount,
                Metadata = new Dictionary<string, string> { ["app"] = _appTag, ["reason"] = Truncate(reason, 400) },
            }, new RequestOptions { IdempotencyKey = idempotencyKey }, ct);
            return refund.Id;
        }
        catch (StripeException e)
        {
            logger.LogWarning("Stripe refused the refund of {PaymentIntentId}: {Code} {Message}", paymentIntentId, e.StripeError?.Code, e.StripeError?.Message);
            throw new PaymentProviderException(e.StripeError?.Message ?? "Couldn't refund the payment", e);
        }
    }

    public async Task<ChargeSnapshot> GetChargeAsync(string chargeId, CancellationToken ct)
    {
        try
        {
            var charge = await Client.V1.Charges.GetAsync(chargeId, new ChargeGetOptions { Expand = ["balance_transaction"] }, cancellationToken: ct);
            return ToSnapshot(charge);
        }
        catch (StripeException e)
        {
            throw new PaymentProviderException(e.StripeError?.Message ?? "Couldn't read the payment", e);
        }
    }

    public async Task<TransferResult> CreateTransferAsync(TransferRequest request, CancellationToken ct)
    {
        var metadata = new Dictionary<string, string>(request.Metadata) { ["app"] = _appTag, ["transfer_id"] = request.TransferId };
        Transfer transfer;
        try
        {
            transfer = await Client.V1.Transfers.CreateAsync(new TransferCreateOptions
            {
                Amount = request.Amount,
                Currency = request.Currency.ToLowerInvariant(),
                Destination = request.DestinationAccountId,
                // Funded by the buyer's charge: possible while its funds are still pending, and tied to it in reports.
                SourceTransaction = request.SourceChargeId,
                TransferGroup = request.TransferGroup,
                Description = Truncate(request.Description, 300),
                Metadata = metadata,
            }, new RequestOptions { IdempotencyKey = $"tr-{request.TransferId}" }, ct);
        }
        catch (StripeException e)
        {
            logger.LogWarning("Stripe refused the transfer {TransferId}: {Code} {Message}", request.TransferId, e.StripeError?.Code, e.StripeError?.Message);
            throw new PaymentProviderException(e.StripeError?.Message ?? "Couldn't pay the seller", e);
        }

        // What reached the seller's balance (in their currency – a USD transfer to an Irish seller arrives in EUR).
        try
        {
            if (transfer.DestinationPaymentId is { } paymentId)
            {
                var arrived = await Client.V1.Charges.GetAsync(paymentId, new ChargeGetOptions { Expand = ["balance_transaction"] },
                    new RequestOptions { StripeAccount = request.DestinationAccountId }, ct);
                if (arrived.BalanceTransaction is { } bt)
                    return new TransferResult(transfer.Id, bt.Amount, bt.Currency?.ToUpperInvariant());
            }
        }
        catch (StripeException e)
        {
            logger.LogInformation("Transfer {TransferId} sent; the arrived amount isn't known yet: {Message}", transfer.Id, e.StripeError?.Message);
        }
        return new TransferResult(transfer.Id, null, null);
    }

    public async Task<string> ReverseTransferAsync(string transferId, long amount, string reason, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            var reversal = await Client.V1.Transfers.Reversals.CreateAsync(transferId, new TransferReversalCreateOptions
            {
                Amount = amount,
                Metadata = new Dictionary<string, string> { ["app"] = _appTag, ["reason"] = Truncate(reason, 400) },
            }, new RequestOptions { IdempotencyKey = idempotencyKey }, ct);
            return reversal.Id;
        }
        catch (StripeException e)
        {
            logger.LogWarning("Stripe refused to reverse transfer {TransferId}: {Code} {Message}", transferId, e.StripeError?.Code, e.StripeError?.Message);
            throw new PaymentProviderException(e.StripeError?.Message ?? "Couldn't take the payout back", e);
        }
    }

    private static ChargeSnapshot ToSnapshot(Charge c)
    {
        var card = c.PaymentMethodDetails?.Card;
        var bt = c.BalanceTransaction;
        return new ChargeSnapshot(c.Id, c.PaymentIntentId, c.Amount, c.Currency?.ToUpperInvariant() ?? "", c.AmountRefunded, c.ReceiptUrl,
            card is null ? null : $"{card.Brand} •••• {card.Last4}",
            bt?.Amount, bt?.Currency?.ToUpperInvariant(), bt?.ExchangeRate);
    }

    private static string Truncate(string text, int max) => text.Length > max ? text[..max] : text;

    public PaymentWebhookEvent ParseWebhook(string payload, string? signatureHeader)
    {
        var secret = stripe.Value.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret)) throw new WebhookSignatureException("Stripe:WebhookSecret is not set");
        if (string.IsNullOrWhiteSpace(signatureHeader)) throw new WebhookSignatureException("Missing Stripe-Signature header");

        Event evt;
        try
        {
            // The SDK's API version can differ from the account's default (the account is shared): don't reject on that.
            evt = EventUtility.ConstructEvent(payload, signatureHeader, secret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException e)
        {
            throw new WebhookSignatureException(e.Message);
        }

        var metadata = (evt.Data?.Object as IHasMetadata)?.Metadata;
        var owned = metadata is not null && metadata.TryGetValue("app", out var app) && app == _appTag;
        var account = evt.Data?.Object is Account a ? ToSnapshot(a) : null;
        var payment = evt.Data?.Object is PaymentIntent pi
            ? new PaymentIntentSnapshot(pi.Id, pi.Status, pi.Amount, pi.Currency, pi.LatestChargeId, pi.LastPaymentError?.Message,
                pi.Metadata?.GetValueOrDefault("payment_id"))
            : null;
        var charge = evt.Data?.Object is Charge ch ? ToSnapshot(ch) : null;
        var dispute = evt.Data?.Object is Dispute d
            ? new DisputeSnapshot(d.Id, d.ChargeId, d.PaymentIntentId, d.Status, d.Reason, d.Amount, d.Currency?.ToUpperInvariant() ?? "")
            : null;
        var transfer = evt.Data?.Object is Transfer tr ? new TransferSnapshot(tr.Id, tr.Amount, tr.AmountReversed) : null;
        return new PaymentWebhookEvent(evt.Id, evt.Type, owned, account, payment, charge, dispute, transfer);
    }

    private SellerAccountSnapshot ToSnapshot(Account a) => new(
        a.Id,
        a.Country?.ToUpperInvariant() ?? "",
        a.ChargesEnabled,
        a.PayoutsEnabled,
        a.Capabilities?.Transfers == "active",
        // A requested capability has a status (active / pending / inactive); null = not requested.
        a.Capabilities?.CardPayments is not null,
        a.DetailsSubmitted,
        (a.Requirements?.CurrentlyDue ?? []).Concat(a.Requirements?.PastDue ?? []).Distinct().ToList(),
        a.Requirements?.DisabledReason,
        a.Metadata is not null && a.Metadata.TryGetValue("app", out var app) && app == _appTag,
        a.Metadata?.GetValueOrDefault("seller"),
        a.Created == default ? null : a.Created);
}

/// <summary>Used when the Stripe keys aren't set: online payment is off and the current flow is unchanged.</summary>
public class NotConfiguredPaymentProvider : IPaymentProvider
{
    public string Name => "none";
    public bool IsConfigured => false;
    public bool IsTestMode => false;

    private static PaymentProviderException Off() => new("Online payment isn't set up on this server.");

    public Task<SellerAccountSnapshot> CreateSellerAccountAsync(NewSellerAccount account, CancellationToken ct) => throw Off();
    public Task<SellerAccountSnapshot> GetSellerAccountAsync(string accountId, CancellationToken ct) => throw Off();
    public Task<IReadOnlyList<SellerAccountSnapshot>> FindSellerAccountsAsync(string? seller, CancellationToken ct) => throw Off();
    public Task<string> CreateOnboardingLinkAsync(string accountId, string returnUrl, string refreshUrl, CancellationToken ct) => throw Off();
    public Task<string> CreateDashboardLinkAsync(string accountId, CancellationToken ct) => throw Off();
    public Task<string> CreateCustomerAsync(string buyer, string? email, CancellationToken ct) => throw Off();
    public Task<string> CreateCustomerSessionKeyAsync(string customerId, CancellationToken ct) => throw Off();
    public Task<NewPaymentIntent> CreatePaymentIntentAsync(PaymentIntentRequest request, CancellationToken ct) => throw Off();
    public Task CancelPaymentIntentAsync(string paymentIntentId, CancellationToken ct) => Task.CompletedTask;
    public Task<string> RefundAsync(string paymentIntentId, long? amount, string reason, string idempotencyKey, CancellationToken ct) => throw Off();
    public Task<ChargeSnapshot> GetChargeAsync(string chargeId, CancellationToken ct) => throw Off();
    public Task<TransferResult> CreateTransferAsync(TransferRequest request, CancellationToken ct) => throw Off();
    public Task<string> ReverseTransferAsync(string transferId, long amount, string reason, string idempotencyKey, CancellationToken ct) => throw Off();
    public PaymentWebhookEvent ParseWebhook(string payload, string? signatureHeader) => throw new WebhookSignatureException("Online payment isn't set up");
}
