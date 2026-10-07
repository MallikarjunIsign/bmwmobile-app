namespace BidService.Models;

/// <summary>BidService's local read model of an auction (filled from AuctionCreated/AuctionUpdated or gRPC).</summary>
public class Auction
{
    public required string Id { get; set; }
    public DateTime AuctionEnd { get; set; }
    public required string Seller { get; set; }
    public int StartingPrice { get; set; } = 1;
    public int ReservePrice { get; set; }
    public int BidIncrement { get; set; } = 1;
    public AuctionStatus Status { get; set; } = AuctionStatus.Live;
    /// <summary>While ReserveNotMet: the seller can accept the high bid until this time.</summary>
    public DateTime? DecisionDeadline { get; set; }
    /// <summary>Kept for compatibility: true once Status is no longer Live.</summary>
    public bool Finished { get; set; }
}