namespace Contracts;

/// <summary>Published by BidService one hour before a Live auction ends ("auction ending in 1 hour").</summary>
public class AuctionEndingSoon
{
    public required string AuctionId { get; set; }
    public required string Seller { get; set; }
    public DateTime AuctionEnd { get; set; }
    /// <summary>Everyone who has bid on it.</summary>
    public List<string> Bidders { get; set; } = [];
    public string? HighBidder { get; set; }
    public int? CurrentHighBid { get; set; }
}
