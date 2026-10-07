namespace Contracts;

/// <summary>Published by BidService when anti-sniping extends an auction's end time.</summary>
public class AuctionExtended
{
    public required string AuctionId { get; set; }
    public DateTime AuctionEnd { get; set; }
}