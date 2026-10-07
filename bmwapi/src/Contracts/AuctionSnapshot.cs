namespace Contracts;

/// <summary>
/// Full state of an auction as published by AuctionService (base of AuctionCreated / AuctionUpdated).
/// ReservePrice is internal: BidService needs it, but SearchService and NotificationService must never
/// expose it to clients.
/// </summary>
public abstract class AuctionSnapshot
{
    public required string Id { get; set; }
    public int ReservePrice { get; set; }
    public int StartingPrice { get; set; }
    public int BidIncrement { get; set; }
    public bool ReserveMet { get; set; }
    public required string Seller { get; set; }
    public string? Winner { get; set; }
    public int? SoldAmount { get; set; }
    public int? CurrentHighBid { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime AuctionEnd { get; set; }
    public required string Status { get; set; }
    public string? EndReason { get; set; }
    public DateTime? SellerDecisionDeadline { get; set; }

    public required string ItemType { get; set; }
    public required string Make { get; set; }
    public required string Model { get; set; }
    public int Year { get; set; }
    public int? YearTo { get; set; }
    public string? Color { get; set; }
    public int? Mileage { get; set; }
    public required string Description { get; set; }
    public string? PartName { get; set; }
    public string? PartNumber { get; set; }
    public string? PartCategory { get; set; }
    public string? Condition { get; set; }
    public int? Quantity { get; set; }

    public ContainerInfo? Container { get; set; }

    /// <summary>Primary image URL (empty when the item has no images yet).</summary>
    public string ImageUrl { get; set; } = string.Empty;
    /// <summary>All image URLs, primary first.</summary>
    public List<string> Images { get; set; } = [];
}