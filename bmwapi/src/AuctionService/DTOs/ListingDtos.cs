namespace AuctionService.DTOs;

/// <summary>Part details (parts only). Enum values as strings: BrandType GenuineOem / OemEquivalent / Aftermarket ...</summary>
public class PartDetailsDto
{
    public string? BrandType { get; set; }
    public string? Brand { get; set; }
    /// <summary>Front / Rear.</summary>
    public string? Position { get; set; }
    /// <summary>Left / Right / Centre, as seen from the driver's seat.</summary>
    public string? Side { get; set; }
    /// <summary>New / Used / Refurbished / ForRepair.</summary>
    public string? Condition { get; set; }
    /// <summary>A / B / C (Used only).</summary>
    public string? UsedGrade { get; set; }
    public int? DonorMileage { get; set; }
    public string? Colour { get; set; }
    public string? PaintCode { get; set; }
    public int? Quantity { get; set; }
    public string? Includes { get; set; }
    public string? Warranty { get; set; }
}

public class FitmentDto
{
    public int YearFrom { get; set; }
    public int YearTo { get; set; }
    public int? MakeId { get; set; }
    public string Make { get; set; } = "";
    public int? ModelId { get; set; }
    public string Model { get; set; } = "";
    public string? Trim { get; set; }
    public string? Engine { get; set; }
    public string? Fuel { get; set; }
    public string? Transmission { get; set; }
    public string? Drivetrain { get; set; }
    public string? SteeringSide { get; set; }
    public string? Market { get; set; }
    public string? Note { get; set; }
}

public class PartNumberDto
{
    public string Value { get; set; } = "";
    /// <summary>OEM or Aftermarket.</summary>
    public string Kind { get; set; } = "OEM";
    public string? Normalized { get; set; }
}

public class VehicleDetailsDto
{
    public string? Trim { get; set; }
    public string? BodyType { get; set; }
    public string? Fuel { get; set; }
    public string? EngineSize { get; set; }
    public string? Transmission { get; set; }
    public string? Drivetrain { get; set; }
    public string? SteeringSide { get; set; }
    /// <summary>Full VIN only for the seller; everyone else gets "•••••• + last 6".</summary>
    public string? Vin { get; set; }
    public string? RunningStatus { get; set; }
    public string? MileageUnit { get; set; }
    public string? DamageType { get; set; }
    public List<string> DamageAreas { get; set; } = [];
    public int? Keys { get; set; }
    public int? Owners { get; set; }
    public string? ServiceHistory { get; set; }
    public bool InspectionAllowed { get; set; }
    public Dictionary<string, string> Papers { get; set; } = [];
}

/// <summary>
/// Saving a draft (autosave) or a Live listing without bids. Every field is optional: only the fields sent are
/// changed. Full validation happens on publish.
/// </summary>
public class SaveListingDto
{
    public int? PartTypeId { get; set; }
    public string? TitleExtra { get; set; }
    public string? Description { get; set; }

    // Vehicle identity (vehicles) – also the fallback year/make/model of a part.
    public int? Year { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public string? Colour { get; set; }
    public int? Mileage { get; set; }

    public PartDetailsDto? PartDetails { get; set; }
    public List<FitmentDto>? Fitments { get; set; }
    public List<PartNumberDto>? PartNumbers { get; set; }
    public Dictionary<string, string>? Specs { get; set; }
    public VehicleDetailsDto? Vehicle { get; set; }

    public string? LocationName { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? CountryCode { get; set; }
    /// <summary>Pickup and / or Shipping (at least one).</summary>
    public List<string>? DeliveryOptions { get; set; }
    /// <summary>Shipping: Small, Medium, Large, Freight.</summary>
    public string? SizeClass { get; set; }
    /// <summary>Shipping (US): the package weight in pounds and box size in inches (defaults from the part type).</summary>
    public decimal? WeightLb { get; set; }
    public decimal? LengthIn { get; set; }
    public decimal? WidthIn { get; set; }
    public decimal? HeightIn { get; set; }
    /// <summary>Shipping: the fee in the listing currency; 0 = free shipping.</summary>
    public int? ShippingFee { get; set; }
    /// <summary>true = free shipping (fee 0); false = not free any more (the fee has to be entered again).</summary>
    public bool? FreeShipping { get; set; }
    /// <summary>Pickup, optional: the exact address, only shared with the buyer after the sale.</summary>
    public string? PickupAddress { get; set; }

    /// <summary>The part price (base price) in the seller's currency; the price itself for fixed-price listings.</summary>
    public int? PartPrice { get; set; }
    /// <summary>Older apps: 1, 3, 5, 7 or 10 days from publishing. Newer apps send <see cref="AuctionEnd"/>.</summary>
    public int? DurationDays { get; set; }
    /// <summary>Auction: when it ends, chosen by the seller (UTC; 1 hour to 30 days ahead).</summary>
    public DateTime? AuctionEnd { get; set; }
    /// <summary>Auction or FixedPrice.</summary>
    public string? SaleType { get; set; }
}

/// <summary>Buying units of a fixed-price listing.</summary>
public class BuyListingDto
{
    public int Quantity { get; set; } = 1;
}

public class CreateListingDto
{
    /// <summary>CommonPart, VehicleSpecificPart or Vehicle.</summary>
    public string Kind { get; set; } = "";
    public int? PartTypeId { get; set; }
    public string? CountryCode { get; set; }
    public string? LocationName { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public class AddNoteDto
{
    public string Text { get; set; } = "";
}

/// <summary>One row of "My Listings".</summary>
public class MyListingDto
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    /// <summary>Draft, Live, EndedNoBids, Sold, Completed, Cancelled.</summary>
    public required string State { get; set; }
    public required string Status { get; set; }
    public string? Kind { get; set; }
    public string? ImageUrl { get; set; }
    public int PartPrice { get; set; }
    public int? CurrentHighBid { get; set; }
    public int BidCount { get; set; }
    public DateTime AuctionEnd { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? Currency { get; set; }
    public string? OrderId { get; set; }
    /// <summary>Auction or FixedPrice.</summary>
    public string SaleType { get; set; } = "Auction";
    /// <summary>FixedPrice: units still for sale / sold so far.</summary>
    public int QuantityAvailable { get; set; }
    public int QuantitySold { get; set; }
}
