namespace Contracts;

/// <summary>
/// Published by BidService when an auction ends. May be published twice for the same auction:
/// first with Status = ReserveNotMet, later with Sold (seller accepted) or Unsold (grace period expired).
/// Consumers must apply it idempotently.
/// </summary>
public class AuctionFinished
{
    public bool ItemSold { get; set; }
    public required string AuctionId { get; set; }
    public string? Winner { get; set; }
    public required string Seller { get; set; }
    public int? Amount { get; set; }
    /// <summary>Sold, ReserveNotMet or Unsold. Null only on messages from older publishers.</summary>
    public string? Status { get; set; }
    /// <summary>TimeEnded, SellerAccepted, SellerAcceptedAfterEnd or GracePeriodExpired.</summary>
    public string? EndReason { get; set; }
    /// <summary>When Status = ReserveNotMet: until when the seller may accept the high bid.</summary>
    public DateTime? SellerDecisionDeadline { get; set; }
    /// <summary>Id of the winning (or, for ReserveNotMet, the highest) bid.</summary>
    public string? BidId { get; set; }
    /// <summary>Everyone who bid (for "you won" / "auction lost" notifications).</summary>
    public List<string> Bidders { get; set; } = [];
}