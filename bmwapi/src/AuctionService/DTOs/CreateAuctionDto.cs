using AuctionService.Entities;

namespace AuctionService.DTOs;

/// <summary>
/// Type-specific rules (vehicle vs part, pricing) are checked by <see cref="Validation.AuctionValidator"/>
/// against the resulting entity, so create and update share one set of rules.
/// </summary>
public class CreateAuctionDto
{
    public ItemType ItemType { get; set; } = ItemType.Vehicle;
    public string Make { get; set; } = "";
    public string Model { get; set; } = "";
    public int Year { get; set; }
    public int? YearTo { get; set; }
    public string? Color { get; set; }
    public int? Mileage { get; set; }
    public string Description { get; set; } = "";

    public string? PartName { get; set; }
    public string? PartNumber { get; set; }
    public PartCategory? PartCategory { get; set; }
    public ItemCondition? Condition { get; set; }
    public int? Quantity { get; set; }

    public string? ContainerId { get; set; }

    /// <summary>Where the item is. Optional: defaults to the seller's primary location (from the sign-in token).</summary>
    public string? LocationName { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public int StartingPrice { get; set; }
    /// <summary>Optional; defaults to StartingPrice (no hidden reserve).</summary>
    public int? ReservePrice { get; set; }
    public int BidIncrement { get; set; }
    public DateTime AuctionEnd { get; set; }
}