using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuctionService.Entities;

[Table("Items")]
public class Item
{
    [MaxLength(36)]
    public string Id { get; set; } =  Guid.NewGuid().ToString();
    public ItemType ItemType { get; set; } = ItemType.Vehicle;

    // For a Vehicle these describe the vehicle; for a Part they describe the vehicle it fits.
    [MaxLength(100)]
    public required string Make { get; set; }
    [MaxLength(100)]
    public required string Model { get; set; }
    public int Year { get; set; }
    // Part only: optional end of the "fits" year range (Year..YearTo).
    public int? YearTo { get; set; }

    // Required for vehicles, optional for parts.
    [MaxLength(100)]
    public string? Color { get; set; }
    public int? Mileage { get; set; }

    [MaxLength(2000)]
    public required string Description { get; set; }

    // Part-only fields (null for vehicles).
    [MaxLength(200)]
    public string? PartName { get; set; }
    [MaxLength(100)]
    public string? PartNumber { get; set; }
    public PartCategory? PartCategory { get; set; }
    public ItemCondition? Condition { get; set; }
    public int? Quantity { get; set; }

    [MaxLength(36)]
    public string? ContainerId { get; set; }
    public Container? Container { get; set; }

    // Where the item is, for "parts within X km". Defaults to the seller's primary location; null for older listings.
    [MaxLength(120)]
    public string? LocationName { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public List<ItemImage> Images { get; set; } = [];

    // ── Listing model (design doc §8). Null on listings created before it. ──
    /// <summary>Common part, vehicle-specific part or whole vehicle.</summary>
    public ListingKind? Kind { get; set; }
    public FitmentMode? FitmentMode { get; set; }
    public int? PartTypeId { get; set; }
    public PartType? PartType { get; set; }
    /// <summary>Generated title (can't be edited) + the seller's extra words.</summary>
    [MaxLength(250)]
    public string? Title { get; set; }
    [MaxLength(80)]
    public string? TitleExtra { get; set; }
    /// <summary>Seller's country (IN, US, NG, IE) and the listing currency (INR, USD, NGN, EUR).</summary>
    [MaxLength(2)]
    public string? CountryCode { get; set; }
    [MaxLength(3)]
    public string? Currency { get; set; }
    public DeliveryOptions DeliveryOptions { get; set; }
    /// <summary>Shipping only: the fee in the listing currency (0 = free shipping). Ships within the listing country.</summary>
    public int? ShippingFee { get; set; }
    /// <summary>Pickup only, optional: the exact address – only the buyer sees it, after the sale. Publicly: the city.</summary>
    [MaxLength(300)]
    public string? PickupAddress { get; set; }
    public SizeClass? SizeClass { get; set; }
    /// <summary>1, 3, 5, 7 or 10 days (the end is set when the listing is published).</summary>
    public int? DurationDays { get; set; }

    public PartDetails? PartDetails { get; set; }
    public List<PartFitment> Fitments { get; set; } = [];
    public List<PartNumber> PartNumbers { get; set; } = [];
    public List<PartSpec> Specs { get; set; } = [];
    public VehicleDetails? VehicleDetails { get; set; }

    // navigation property
    public Auction? Auction { get; set; } = null;

    [NotMapped]
    public ItemImage? PrimaryImage =>
        Images.FirstOrDefault(x => x.IsPrimary) ?? Images.OrderBy(x => x.SortOrder).FirstOrDefault();

    [NotMapped]
    public IEnumerable<ItemImage> OrderedImages =>
        Images.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.SortOrder);

    [NotMapped]
    public string DisplayName => ItemType == ItemType.Part && !string.IsNullOrWhiteSpace(PartName)
        ? $"{PartName} (fits {Make} {Model})"
        : $"{Year} {Make} {Model}";

    // Removes part-only fields so a Vehicle never carries part data.
    public void ClearPartFields()
    {
        PartName = null;
        PartNumber = null;
        PartCategory = null;
        Condition = null;
        Quantity = null;
        YearTo = null;
    }
}