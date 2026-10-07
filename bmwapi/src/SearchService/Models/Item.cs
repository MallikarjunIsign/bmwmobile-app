using Contracts;

namespace SearchService.Models;

/// <summary>
/// Meilisearch document (mirrors AuctionService's public AuctionDto). Deliberately has no ReservePrice:
/// the reserve is never indexed; only ReserveMet is.
/// </summary>
public class Item
{
    public required string Id { get; set; }
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

    public string ItemType { get; set; } = "Vehicle";
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

    public string ImageUrl { get; set; } = string.Empty;
    public List<string> Images { get; set; } = [];
}