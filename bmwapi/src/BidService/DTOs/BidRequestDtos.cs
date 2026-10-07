namespace BidService.DTOs;

public class AcceptBidDto
{
    public string AuctionId { get; set; } = "";
    public string BidId { get; set; } = "";
}

public class NextMinimumBidDto
{
    public int MinimumBid { get; set; }
    public int? CurrentHighBid { get; set; }
    public int BidIncrement { get; set; }
    public bool ReserveMet { get; set; }
    public DateTime AuctionEnd { get; set; }
    public required string Status { get; set; }
}