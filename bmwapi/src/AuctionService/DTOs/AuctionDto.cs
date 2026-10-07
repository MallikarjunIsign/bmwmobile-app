using System.Text.Json.Serialization;
using Contracts;

namespace AuctionService.DTOs;

public class AuctionDto
{
    public required string Id { get; set; }
    public int StartingPrice { get; set; }
    public int BidIncrement { get; set; }
    /// <summary>Only returned to the auction's seller; omitted from every other response.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ReservePrice { get; set; }
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

    public string? ContainerId { get; set; }
    public ContainerInfo? Container { get; set; }

    public string? LocationName { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>Primary image URL (empty when there are no images).</summary>
    public string ImageUrl { get; set; } = string.Empty;
    /// <summary>Image URLs, primary first.</summary>
    public List<string> Images { get; set; } = [];
    /// <summary>Same images with ids, for the seller's image manager.</summary>
    public List<ItemImageDto> ImageItems { get; set; } = [];
}

public class ItemImageDto
{
    public required string Id { get; set; }
    public required string Url { get; set; }
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
}