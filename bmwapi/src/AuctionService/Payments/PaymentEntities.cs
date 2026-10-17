using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuctionService.Payments;

// All amounts are in the currency's smallest unit (cents) with the currency next to them – never floating point.

/// <summary>Not started / Pending verification / Active / Action needed (what the seller sees).</summary>
public enum SellerPaymentStatus
{
    NotStarted,
    Pending,
    Active,
    ActionNeeded,
}

/// <summary>A seller's payout account at the provider (Stripe: an Express connected account).</summary>
[Table("SellerPaymentAccounts")]
public class SellerPaymentAccount
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>The seller's username (one account per seller).</summary>
    [MaxLength(100)]
    public required string Seller { get; set; }
    [MaxLength(20)]
    public string Provider { get; set; } = "stripe";
    /// <summary>acct_… (Stripe).</summary>
    [MaxLength(100)]
    public required string ProviderAccountId { get; set; }
    /// <summary>US or IE.</summary>
    [MaxLength(2)]
    public required string Country { get; set; }
    public bool ChargesEnabled { get; set; }
    public bool PayoutsEnabled { get; set; }
    public bool TransfersActive { get; set; }
    public bool CardPaymentsRequested { get; set; }
    public bool DetailsSubmitted { get; set; }
    public SellerPaymentStatus Status { get; set; }
    /// <summary>What the provider still needs (comma-separated requirement names).</summary>
    [MaxLength(2000)]
    public string? RequirementsDue { get; set; }
    [MaxLength(200)]
    public string? DisabledReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A buyer's customer record at the provider (saved cards).</summary>
[Table("BuyerPaymentCustomers")]
public class BuyerPaymentCustomer
{
    [Key, MaxLength(100)]
    public required string Buyer { get; set; }
    [MaxLength(20)]
    public string Provider { get; set; } = "stripe";
    /// <summary>cus_… (Stripe).</summary>
    [MaxLength(100)]
    public required string ProviderCustomerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum PaymentStatus
{
    /// <summary>Created; the buyer hasn't paid yet.</summary>
    Pending,
    Succeeded,
    Failed,
    Canceled,
    Refunded,
    PartiallyRefunded,
    Disputed,
}

/// <summary>
/// What a buyer pays: item + shipping, and the app's commission on the item. Buy now: made for a reservation, the order
/// only exists once it is paid. Auction / second-chance: made for the order that waits for payment.
/// </summary>
[Table("Payments")]
public class Payment
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>The order (auction / second chance), or the order created when a Buy now payment succeeds.</summary>
    [MaxLength(36)]
    public string? OrderId { get; set; }
    /// <summary>Buy now: the reservation this payment is for.</summary>
    [MaxLength(36)]
    public string? ReservationId { get; set; }
    [MaxLength(36)]
    public required string AuctionId { get; set; }
    [MaxLength(100)]
    public required string Buyer { get; set; }
    [MaxLength(100)]
    public required string Seller { get; set; }
    [MaxLength(20)]
    public string Provider { get; set; } = "stripe";
    /// <summary>pi_… (Stripe PaymentIntent).</summary>
    [MaxLength(100)]
    public string? ProviderPaymentId { get; set; }
    /// <summary>ch_… (the successful charge; the transfer's source).</summary>
    [MaxLength(100)]
    public string? ProviderChargeId { get; set; }
    /// <summary>USD or EUR.</summary>
    [MaxLength(3)]
    public required string Currency { get; set; }
    public long ItemAmount { get; set; }
    public long ShippingAmount { get; set; }
    public long TotalAmount { get; set; }
    public long CommissionAmount { get; set; }
    [Column(TypeName = "numeric(5,2)")]
    public decimal CommissionPercent { get; set; }
    public PaymentStatus Status { get; set; }
    /// <summary>Groups the charge and the seller transfer at the provider ("order_…").</summary>
    [MaxLength(100)]
    public string? TransferGroup { get; set; }
    [MaxLength(500)]
    public string? ReceiptUrl { get; set; }
    [MaxLength(300)]
    public string? FailureMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }

    /// <summary>Refunded so far (cents of <see cref="Currency"/>).</summary>
    public long RefundedAmount { get; set; }
    /// <summary>
    /// What the charge became in the platform's balance (USD): a EUR payment is converted by the provider. The seller's
    /// transfer is made in this currency (and converted back for Irish sellers – "about €…").
    /// </summary>
    public long? BalanceAmount { get; set; }
    [MaxLength(3)]
    public string? BalanceCurrency { get; set; }
    [Column(TypeName = "numeric(18,8)")]
    public decimal? ExchangeRate { get; set; }
    /// <summary>The card used ("visa •••• 4242"), for the receipt.</summary>
    [MaxLength(60)]
    public string? CardLabel { get; set; }
    /// <summary>dp_… and its status (warning_needs_response, under_review, won, lost …) while the buyer's bank disputes it.</summary>
    [MaxLength(100)]
    public string? ProviderDisputeId { get; set; }
    [MaxLength(40)]
    public string? DisputeStatus { get; set; }
    [MaxLength(100)]
    public string? DisputeReason { get; set; }
}

public enum TransferStatus
{
    /// <summary>To be sent (the seller's payment account isn't ready yet, or the provider failed: retried by the job).</summary>
    Pending,
    /// <summary>Held: the order is on hold or disputed.</summary>
    Frozen,
    Transferred,
    /// <summary>Taken back from the seller in full (refund / lost dispute).</summary>
    Reversed,
    Failed,
    /// <summary>Not sent: the order was refunded in full before completion.</summary>
    Cancelled,
}

/// <summary>
/// The seller's share of a payment: item + shipping − commission − refunds, sent when the order is completed.
/// Amount / Currency is what is transferred (the platform's balance currency, USD); SellerAmount / SellerCurrency is the
/// share in the listing's currency (EUR for Irish sellers: they receive "about" that after the provider's conversion).
/// </summary>
[Table("PaymentTransfers")]
public class PaymentTransfer
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [MaxLength(36)]
    public required string OrderId { get; set; }
    [MaxLength(36)]
    public required string PaymentId { get; set; }
    [MaxLength(100)]
    public required string Seller { get; set; }
    [MaxLength(100)]
    public string? ProviderAccountId { get; set; }
    /// <summary>tr_… (Stripe).</summary>
    [MaxLength(100)]
    public string? ProviderTransferId { get; set; }
    public long Amount { get; set; }
    [MaxLength(3)]
    public required string Currency { get; set; }
    public long ReversedAmount { get; set; }
    public TransferStatus Status { get; set; }
    [MaxLength(300)]
    public string? FailureMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? TransferredAt { get; set; }

    /// <summary>The share in the listing's currency (cents): what the seller is told ("about €94.05" for EUR).</summary>
    public long SellerAmount { get; set; }
    [MaxLength(3)]
    public string SellerCurrency { get; set; } = "USD";
    /// <summary>What arrived in the seller's account (cents, their currency) when the provider says so.</summary>
    public long? ArrivedAmount { get; set; }
    [MaxLength(3)]
    public string? ArrivedCurrency { get; set; }
    /// <summary>How many times sending was attempted (the job retries Pending transfers).</summary>
    public int Attempts { get; set; }
    public DateTime? LastAttemptAt { get; set; }
}

/// <summary>A refund to the buyer's original card (full or partial).</summary>
[Table("PaymentRefunds")]
public class PaymentRefund
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [MaxLength(36)]
    public required string PaymentId { get; set; }
    [MaxLength(36)]
    public required string OrderId { get; set; }
    /// <summary>re_… (Stripe).</summary>
    [MaxLength(100)]
    public string? ProviderRefundId { get; set; }
    public long Amount { get; set; }
    [MaxLength(3)]
    public required string Currency { get; set; }
    [MaxLength(300)]
    public required string Reason { get; set; }
    [MaxLength(30)]
    public string Status { get; set; } = "Pending";
    [MaxLength(100)]
    public required string CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Webhook events already handled (the provider can send an event more than once).</summary>
[Table("PaymentWebhookEvents")]
public class ProcessedWebhookEvent
{
    /// <summary>evt_… (Stripe).</summary>
    [Key, MaxLength(100)]
    public required string EventId { get; set; }
    [MaxLength(20)]
    public string Provider { get; set; } = "stripe";
    [MaxLength(100)]
    public required string Type { get; set; }
    /// <summary>What happened: "handled", "ignored" …</summary>
    [MaxLength(200)]
    public string? Outcome { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

public enum ReservationStatus
{
    Active,
    /// <summary>Paid: became an order.</summary>
    Converted,
    /// <summary>Payment failed / cancelled / expired: the quantity is free again.</summary>
    Released,
}

/// <summary>
/// Buy now: units held for a buyer while they pay (15 minutes by default), with what they chose (pickup / shipping and
/// the fixed shipping charge), so the order can be created exactly like that once the payment succeeds.
/// </summary>
[Table("StockReservations")]
public class StockReservation
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [MaxLength(36)]
    public required string AuctionId { get; set; }
    [MaxLength(100)]
    public required string Buyer { get; set; }
    public int Quantity { get; set; }
    public ReservationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    /// <summary>The order once paid.</summary>
    [MaxLength(36)]
    public string? OrderId { get; set; }

    public Entities.DeliveryOptions DeliveryMethod { get; set; }
    /// <summary>Shipping: the calculated charge (whole currency units) and how it was worked out; 0 for pickup.</summary>
    public int ShippingFee { get; set; }
    public int? ShippingDistance { get; set; }
    [MaxLength(2)]
    public string? ShippingDistanceUnit { get; set; }
    [MaxLength(40)]
    public string? ShippingService { get; set; }
    public int? ShippingZone { get; set; }
    public bool ShippingRateEstimated { get; set; }
    public Entities.AddressInfo? ShipTo { get; set; }
    [MaxLength(200)]
    public string? BuyerEmail { get; set; }
    [MaxLength(30)]
    public string? BuyerPhone { get; set; }
}

public enum SecondChanceStatus
{
    Offered,
    Accepted,
    Declined,
    /// <summary>Not answered / not paid in time (no strike).</summary>
    Expired,
    Paid,
}

/// <summary>The winner didn't pay: the next-highest bidder is offered the item at their own highest bid.</summary>
[Table("SecondChanceOffers")]
public class SecondChanceOffer
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [MaxLength(36)]
    public required string AuctionId { get; set; }
    [MaxLength(100)]
    public required string Bidder { get; set; }
    /// <summary>The bidder's own highest bid (the item price they'd pay).</summary>
    public int Amount { get; set; }
    /// <summary>1 = the first offer after the winner, up to Payments:SecondChanceOffers.</summary>
    public int Round { get; set; }
    public SecondChanceStatus Status { get; set; }
    [MaxLength(36)]
    public string? OrderId { get; set; }
    public DateTime OfferedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? AnsweredAt { get; set; }
}

/// <summary>A winner who didn't pay. Payments:StrikeLimit strikes within Payments:StrikeWindowMonths block bidding.</summary>
[Table("NonPaymentStrikes")]
public class NonPaymentStrike
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [MaxLength(100)]
    public required string User { get; set; }
    [MaxLength(36)]
    public required string AuctionId { get; set; }
    [MaxLength(36)]
    public string? OrderId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClearedAt { get; set; }
    [MaxLength(100)]
    public string? ClearedBy { get; set; }
}
