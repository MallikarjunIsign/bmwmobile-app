namespace AuctionService.Payments;

/// <summary>
/// A payment provider (Stripe now; Razorpay / Paystack later for India / Nigeria). The rest of the service only talks
/// to this interface, so another provider is a new implementation, not a rewrite.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>"stripe".</summary>
    string Name { get; }

    /// <summary>False without keys: online payment is off and the current flow is used.</summary>
    bool IsConfigured { get; }

    /// <summary>Test keys (sk_test_) or a test provider: test-only settings may apply.</summary>
    bool IsTestMode { get; }

    // ── sellers ──

    /// <summary>A seller's payout account (Stripe: an Express connected account), tagged with the app tag.</summary>
    Task<SellerAccountSnapshot> CreateSellerAccountAsync(NewSellerAccount account, CancellationToken ct);

    Task<SellerAccountSnapshot> GetSellerAccountAsync(string accountId, CancellationToken ct);

    /// <summary>
    /// The sellers' accounts that already exist at the provider, tagged with the app tag (and this seller when given) –
    /// read only, nothing is created. Used to link an account made while another database was in use.
    /// </summary>
    Task<IReadOnlyList<SellerAccountSnapshot>> FindSellerAccountsAsync(string? seller, CancellationToken ct);

    /// <summary>The provider-hosted onboarding page (one-time link).</summary>
    Task<string> CreateOnboardingLinkAsync(string accountId, string returnUrl, string refreshUrl, CancellationToken ct);

    /// <summary>The provider-hosted seller dashboard (payouts, bank account).</summary>
    Task<string> CreateDashboardLinkAsync(string accountId, CancellationToken ct);

    // ── buyers ──

    /// <summary>The buyer's customer record (saved cards), created when missing. Tagged with the app tag.</summary>
    Task<string> CreateCustomerAsync(string buyer, string? email, CancellationToken ct);

    /// <summary>A short-lived key so the app's payment sheet can show the buyer's saved cards.</summary>
    Task<string> CreateCustomerSessionKeyAsync(string customerId, CancellationToken ct);

    /// <summary>
    /// A payment on the PLATFORM account (separate charges and transfers): the app confirms it with the payment sheet.
    /// The amount is calculated by the backend; nothing comes from the app.
    /// </summary>
    Task<NewPaymentIntent> CreatePaymentIntentAsync(PaymentIntentRequest request, CancellationToken ct);

    /// <summary>Stops a payment that wasn't completed (reservation expired / cancelled). Already final → ignored.</summary>
    Task CancelPaymentIntentAsync(string paymentIntentId, CancellationToken ct);

    /// <summary>
    /// Refund to the original card: the whole payment (amount null) or part of it, in the payment's currency.
    /// The idempotency key makes a retried refund happen only once.
    /// </summary>
    Task<string> RefundAsync(string paymentIntentId, long? amount, string reason, string idempotencyKey, CancellationToken ct);

    /// <summary>The successful charge: receipt link, card, and what it became in the platform's balance (currency conversion).</summary>
    Task<ChargeSnapshot> GetChargeAsync(string chargeId, CancellationToken ct);

    // ── seller payouts ──

    /// <summary>
    /// Sends the seller's share to their account (separate charges and transfers), funded by the buyer's charge
    /// (source transaction), so it works before the charge's funds are available. Idempotent per transfer id.
    /// </summary>
    Task<TransferResult> CreateTransferAsync(TransferRequest request, CancellationToken ct);

    /// <summary>Takes (part of) a transfer back from the seller (refund after payout, lost dispute).</summary>
    Task<string> ReverseTransferAsync(string transferId, long amount, string reason, string idempotencyKey, CancellationToken ct);

    // ── webhooks ──

    /// <summary>
    /// Verifies the webhook's signature and reads it. Throws <see cref="WebhookSignatureException"/> when the signature
    /// is wrong (or the webhook secret isn't set).
    /// </summary>
    PaymentWebhookEvent ParseWebhook(string payload, string? signatureHeader);
}

public record NewSellerAccount(string Seller, string Country, string? Email, string? DisplayName = null);

/// <summary>What the provider says about a seller's account (only what the app needs).</summary>
public record SellerAccountSnapshot(
    string AccountId,
    string Country,
    bool ChargesEnabled,
    bool PayoutsEnabled,
    bool TransfersActive,
    bool CardPaymentsRequested,
    bool DetailsSubmitted,
    IReadOnlyList<string> RequirementsDue,
    string? DisabledReason,
    // Tagged app=shopcarparts (created by this app, not the other project on the shared account).
    bool OwnedByApp,
    // The seller's username from the account's metadata (seller=…), and when the account was created.
    string? Seller = null,
    DateTime? Created = null);

/// <summary>Amount in the smallest unit (cents); currency "usd" / "eur".</summary>
public record PaymentIntentRequest(
    string PaymentId,
    long Amount,
    string Currency,
    string CustomerId,
    string TransferGroup,
    string Description,
    string? ReceiptEmail,
    IReadOnlyDictionary<string, string> Metadata);

public record NewPaymentIntent(string Id, string ClientSecret);

/// <summary>What a webhook says about a payment.</summary>
public record PaymentIntentSnapshot(
    string Id,
    // succeeded, requires_payment_method (failed attempt), canceled, processing …
    string Status,
    long Amount,
    string Currency,
    string? LatestChargeId,
    string? FailureMessage,
    string? PaymentId);

/// <summary>A successful charge (amounts in cents). Balance* = what it became in the platform's balance currency.</summary>
public record ChargeSnapshot(
    string Id,
    string? PaymentIntentId,
    long Amount,
    string Currency,
    long AmountRefunded,
    string? ReceiptUrl,
    string? CardLabel,
    long? BalanceAmount = null,
    string? BalanceCurrency = null,
    decimal? ExchangeRate = null);

/// <summary>A seller transfer (cents of <see cref="Currency"/>, the platform's balance currency).</summary>
public record TransferRequest(
    string TransferId,
    long Amount,
    string Currency,
    string DestinationAccountId,
    string? SourceChargeId,
    string TransferGroup,
    string Description,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>tr_… and, when the provider says, what arrived in the seller's account (their currency, cents).</summary>
public record TransferResult(string Id, long? ArrivedAmount, string? ArrivedCurrency);

/// <summary>A buyer's bank disputing a charge (chargeback).</summary>
public record DisputeSnapshot(string Id, string ChargeId, string? PaymentIntentId, string Status, string? Reason, long Amount, string Currency);

/// <summary>A transfer's state (transfer.reversed).</summary>
public record TransferSnapshot(string Id, long Amount, long AmountReversed, string? TransferId = null);

/// <summary>
/// A verified webhook event. Account is set for account.updated, Payment for payment_intent.*, Charge for charge.refunded,
/// Dispute for charge.dispute.*, Transfer for transfer.*. Disputes carry no metadata: the webhook service checks the
/// charge is one of ours instead of the app tag.
/// </summary>
public record PaymentWebhookEvent(string Id, string Type, bool OwnedByApp, SellerAccountSnapshot? Account,
    PaymentIntentSnapshot? Payment = null, ChargeSnapshot? Charge = null, DisputeSnapshot? Dispute = null,
    TransferSnapshot? Transfer = null);

public class WebhookSignatureException(string message) : Exception(message);

/// <summary>The provider refused or failed a call; the message is safe to show.</summary>
public class PaymentProviderException(string message, Exception? inner = null) : Exception(message, inner);
