using AuctionService.Entities;

namespace AuctionService.DTOs;

/// <summary>
/// Partial update: null means "leave unchanged". To clear an optional field send an empty string
/// (PartNumber, ContainerId) or 0 (YearTo, Mileage).
/// </summary>
public class UpdateAuctionDto
{
    public ItemType? ItemType { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public string? Description { get; set; }
    public int? Year { get; set; }
    public int? YearTo { get; set; }
    public string? Color { get; set; }
    public int? Mileage { get; set; }

    public string? PartName { get; set; }
    public string? PartNumber { get; set; }
    public PartCategory? PartCategory { get; set; }
    public ItemCondition? Condition { get; set; }
    public int? Quantity { get; set; }

    public string? ContainerId { get; set; }

    public int? StartingPrice { get; set; }
    public int? ReservePrice { get; set; }
    public int? BidIncrement { get; set; }
    public DateTime? AuctionEnd { get; set; }
}