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

    /// <summary>Where the item is (null for older listings). SearchService uses it for "parts within X km".</summary>
    public string? LocationName { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>Primary image URL (empty when the item has no images yet).</summary>
    public string ImageUrl { get; set; } = string.Empty;
    /// <summary>All image URLs, primary first.</summary>
    public List<string> Images { get; set; } = [];

    // ── Listing model (design doc §7.4). Null/empty on listings created before it. ──
    /// <summary>CommonPart, VehicleSpecificPart or Vehicle.</summary>
    public string? Kind { get; set; }
    /// <summary>Universal, SizeBased or VehicleSpecific.</summary>
    public string? FitmentMode { get; set; }
    public int? PartTypeId { get; set; }
    public string? PartType { get; set; }
    /// <summary>Top-level category name ("Body – Exterior").</summary>
    public string? Category { get; set; }
    public string? Title { get; set; }
    /// <summary>"Fits Toyota Hilux 2016–2020 (+1 more)", "Universal – fits any vehicle" …</summary>
    public string? FitmentSummary { get; set; }
    public List<FitmentInfo> Fitments { get; set; } = [];
    /// <summary>Part numbers as typed (OEM first).</summary>
    public List<string> PartNumbers { get; set; } = [];
    /// <summary>Spec key → value (tyre width → 265 ...).</summary>
    public Dictionary<string, string> Specs { get; set; } = [];
    public string? BrandType { get; set; }
    public string? Brand { get; set; }
    public string? Position { get; set; }
    public string? Side { get; set; }
    public string? UsedGrade { get; set; }
    public string? CountryCode { get; set; }
    public string? Currency { get; set; }
    /// <summary>Pickup and / or Shipping.</summary>
    public List<string> DeliveryOptions { get; set; } = [];
    /// <summary>Shipping fee (0 = free), null without shipping.</summary>
    public int? ShippingFee { get; set; }
    /// <summary>Pickup city shown publicly ("Hyderabad"); never the exact address.</summary>
    public string? PickupCity { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int BidCount { get; set; }
    public VehicleSummary? Vehicle { get; set; }

    /// <summary>Auction (bidding until AuctionEnd) or FixedPrice (buy now at StartingPrice until sold out or cancelled).</summary>
    public string SaleType { get; set; } = "Auction";
    /// <summary>The seller's display name ("Ravi Kumar"); null on older listings (use Seller).</summary>
    public string? SellerName { get; set; }
    /// <summary>FixedPrice: units still for sale.</summary>
    public int QuantityAvailable { get; set; }
    /// <summary>The seller's average rating (null: no reviews yet) and number of ratings.</summary>
    public double? SellerRating { get; set; }
    public int SellerRatingCount { get; set; }
}

/// <summary>One fitment row (vehicle-specific parts). Null optional fields mean "Any".</summary>
public class FitmentInfo
{
    public int YearFrom { get; set; }
    public int YearTo { get; set; }
    public required string Make { get; set; }
    public required string Model { get; set; }
    public string? Trim { get; set; }
    public string? Engine { get; set; }
    public string? Fuel { get; set; }
    public string? Transmission { get; set; }
    public string? Drivetrain { get; set; }
    public string? SteeringSide { get; set; }
}

/// <summary>Public details of a whole vehicle (never the VIN).</summary>
public class VehicleSummary
{
    public string? Trim { get; set; }
    public string? BodyType { get; set; }
    public string? Fuel { get; set; }
    public string? EngineSize { get; set; }
    public string? Transmission { get; set; }
    public string? Drivetrain { get; set; }
    public string? SteeringSide { get; set; }
    public string? RunningStatus { get; set; }
    public string? MileageUnit { get; set; }
    public string? DamageType { get; set; }
}