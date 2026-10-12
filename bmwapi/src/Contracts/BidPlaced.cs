namespace Contracts;

public class BidPlaced
{
    public required string Id { get; set; }
    public required string AuctionId { get; set; }
    public required string Bidder { get; set; }
    public DateTime BidTime { get; set; }
    /// <summary>The bid TOTAL (part price + the amount on top).</summary>
    public int Amount { get; set; }
    public required string BidStatus { get; set; }

    /// <summary>Accepted bids on the auction including this one.</summary>
    public int BidCount { get; set; }
    /// <summary>The auction's seller ("new bid on your listing").</summary>
    public string? Seller { get; set; }
    /// <summary>Who was the highest bidder before this bid ("you've been outbid"); null for the first bid.</summary>
    public string? PreviousHighBidder { get; set; }
}
