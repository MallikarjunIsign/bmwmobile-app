using System.ComponentModel.DataAnnotations;

namespace AuctionService.Entities;

public class Auction
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int StartingPrice { get; set; } = 1;
    // Hidden from buyers. Equal to StartingPrice means "no hidden reserve".
    public int ReservePrice { get; set; } = 1;
    public int BidIncrement { get; set; } = 1;
    [MaxLength(100)]
    public required string Seller { get; set; }
    [MaxLength(100)]
    public string? Winner { get; set; }
    public int? SoldAmount { get; set; }
    public int? CurrentHighBid { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime AuctionEnd { get; set; }
    public Status Status { get; set; }
    public EndReason? EndReason { get; set; }
    // Set while Status = ReserveNotMet: the seller can accept the high bid until this time.
    public DateTime? SellerDecisionDeadline { get; set; }
    public required Item Item { get; set; }

    public bool HasReservePrice() => ReservePrice > StartingPrice;

    public bool IsReserveMet() => CurrentHighBid is not null && CurrentHighBid >= ReservePrice;
}