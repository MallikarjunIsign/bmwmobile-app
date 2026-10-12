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
    /// <summary>Accepted bids so far (from BidPlaced).</summary>
    public int BidCount { get; set; }
    /// <summary>The seller's contact (from the sign-in token), only ever shown to the buyer after a sale.</summary>
    [MaxLength(200)]
    public string? SellerEmail { get; set; }
    [MaxLength(30)]
    public string? SellerPhone { get; set; }
    /// <summary>The seller's display name (the "name" claim), shown on cards; null on older listings.</summary>
    [MaxLength(100)]
    public string? SellerName { get; set; }
    public SaleType SaleType { get; set; } = SaleType.Auction;
    /// <summary>The seller's rating (copied from SellerRatings when it changes), for cards, details and search.</summary>
    public double? SellerRatingAverage { get; set; }
    public int SellerRatingCount { get; set; }
    /// <summary>FixedPrice: units bought so far. A concurrency token, so two buyers can't take the last unit.</summary>
    [ConcurrencyCheck]
    public int QuantitySold { get; set; }
    public required Item Item { get; set; }

    /// <summary>FixedPrice: units listed (parts: the quantity, vehicles: 1).</summary>
    public int QuantityListed => Item.ItemType == ItemType.Vehicle ? 1 : Math.Max(Item.Quantity ?? 1, 1);

    public int QuantityAvailable => SaleType == SaleType.FixedPrice ? Math.Max(QuantityListed - QuantitySold, 0) : 0;

    public bool HasReservePrice() => ReservePrice > StartingPrice;

    public bool IsReserveMet() => CurrentHighBid is not null && CurrentHighBid >= ReservePrice;
}