namespace AuctionService.DTOs;

/// <summary>New terms for relisting an ended auction (the item, images and container are copied).</summary>
public class RelistAuctionDto
{
    public int StartingPrice { get; set; }
    /// <summary>Optional; defaults to StartingPrice (no hidden reserve).</summary>
    public int? ReservePrice { get; set; }
    public int BidIncrement { get; set; }
    public DateTime AuctionEnd { get; set; }
}