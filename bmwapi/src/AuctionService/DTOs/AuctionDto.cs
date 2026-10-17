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

    // ── Listing model ──
    public string? Kind { get; set; }
    public string? FitmentMode { get; set; }
    public int? PartTypeId { get; set; }
    public string? PartType { get; set; }
    public string? Category { get; set; }
    public string? SubCategory { get; set; }
    public string? Title { get; set; }
    public string? TitleExtra { get; set; }
    public string? FitmentSummary { get; set; }
    public string? CountryCode { get; set; }
    public string? Currency { get; set; }
    /// <summary>Pickup and / or Shipping.</summary>
    public List<string> DeliveryOptions { get; set; } = [];
    public string? SizeClass { get; set; }
    /// <summary>The package (lb / inches) the shipping price uses: the seller's values or the part type's defaults.</summary>
    public decimal? WeightLb { get; set; }
    public decimal? LengthIn { get; set; }
    public decimal? WidthIn { get; set; }
    public decimal? HeightIn { get; set; }
    /// <summary>Shipping: the fee (0 = free shipping); null without shipping.</summary>
    public int? ShippingFee { get; set; }
    /// <summary>Shipping: the country it ships within (the listing country).</summary>
    public string? ShipsWithin { get; set; }
    /// <summary>Pickup: the public city ("Hyderabad"); the exact address is only shared with the buyer after the sale.</summary>
    public string? PickupCity { get; set; }
    /// <summary>Seller only: the exact pickup address.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PickupAddress { get; set; }
    public int? DurationDays { get; set; }
    public int BidCount { get; set; }
    public PartDetailsDto? PartDetails { get; set; }
    public List<FitmentDto> Fitments { get; set; } = [];
    /// <summary>Part numbers as typed (OEM first) – same shape as the search document.</summary>
    public List<string> PartNumbers { get; set; } = [];
    /// <summary>Part numbers with their kind (OEM / Aftermarket) and normalised form.</summary>
    public List<PartNumberDto> PartNumberItems { get; set; } = [];
    // Flat copies of the part details (the search index reads these).
    public string? BrandType { get; set; }
    public string? Brand { get; set; }
    public string? Position { get; set; }
    public string? Side { get; set; }
    public string? UsedGrade { get; set; }
    public Dictionary<string, string> Specs { get; set; } = [];
    public VehicleDetailsDto? Vehicle { get; set; }
    public string? ThumbnailUrl { get; set; }
    /// <summary>Seller only: price, fitment and details can still be changed (no bids yet).</summary>
    public bool CanEdit { get; set; }

    /// <summary>Auction or FixedPrice (buy now at StartingPrice until sold out or cancelled).</summary>
    public string SaleType { get; set; } = "Auction";
    /// <summary>The seller's display name; null on older listings (show Seller).</summary>
    public string? SellerName { get; set; }
    /// <summary>FixedPrice: units still for sale / bought so far.</summary>
    public int QuantityAvailable { get; set; }
    public int QuantitySold { get; set; }
    /// <summary>Units held for buyers who are paying right now (Buy now, 15 minutes): "Reserved" when none are left.</summary>
    public int QuantityReserved { get; set; }
    /// <summary>The seller's average rating (null: no reviews yet) and number of ratings – "⭐ 4.7 (32)".</summary>
    public double? SellerRating { get; set; }
    public int SellerRatingCount { get; set; }
}

public class ItemImageDto
{
    public required string Id { get; set; }
    public required string Url { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Angle { get; set; }
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
}