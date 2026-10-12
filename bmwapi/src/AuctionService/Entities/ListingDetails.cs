using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuctionService.Entities;

public enum BrandType
{
    GenuineOem = 0,
    OemEquivalent = 1,
    Aftermarket = 2
}

/// <summary>Used parts: A = very little or no damage, B = minor damage, C = noticeable damage.</summary>
public enum UsedGrade
{
    A = 0,
    B = 1,
    C = 2
}

/// <summary>
/// What the seller offers: Pickup (at the item location), Shipping (within the seller's country, for a fee or free), or
/// both. "Buyer arranges" (4) was removed: those listings and orders were migrated to Pickup.
/// </summary>
[Flags]
public enum DeliveryOptions
{
    None = 0,
    Pickup = 1,
    /// <summary>Was "SellerShips".</summary>
    Shipping = 2
}

public enum SizeClass
{
    Small = 0,
    Medium = 1,
    Large = 2,
    Freight = 3
}

/// <summary>Part details (parts only), one row per item.</summary>
[Table("PartDetails")]
public class PartDetails
{
    [Key, MaxLength(36)]
    public required string ItemId { get; set; }
    public BrandType? BrandType { get; set; }
    [MaxLength(60)]
    public string? Brand { get; set; }
    /// <summary>"Front" or "Rear" (only when the part type has a position).</summary>
    [MaxLength(10)]
    public string? Position { get; set; }
    /// <summary>"Left", "Right" or "Centre", as seen from the driver's seat (never driver/passenger).</summary>
    [MaxLength(10)]
    public string? Side { get; set; }
    public UsedGrade? UsedGrade { get; set; }
    public int? DonorMileage { get; set; }
    [MaxLength(40)]
    public string? Colour { get; set; }
    [MaxLength(40)]
    public string? PaintCode { get; set; }
    [MaxLength(300)]
    public string? Includes { get; set; }
    [MaxLength(300)]
    public string? Warranty { get; set; }
}

/// <summary>One vehicle (year range) a vehicle-specific part fits. Optional fields null = "Any".</summary>
[Table("PartFitments")]
public class PartFitment
{
    public int Id { get; set; }
    [MaxLength(36)]
    public required string ItemId { get; set; }
    public int SortOrder { get; set; }
    public int YearFrom { get; set; }
    public int YearTo { get; set; }
    public int? MakeId { get; set; }
    [MaxLength(60)]
    public required string Make { get; set; }
    public int? ModelId { get; set; }
    [MaxLength(60)]
    public required string Model { get; set; }
    [MaxLength(60)]
    public string? Trim { get; set; }
    [MaxLength(60)]
    public string? Engine { get; set; }
    [MaxLength(20)]
    public string? Fuel { get; set; }
    [MaxLength(20)]
    public string? Transmission { get; set; }
    [MaxLength(20)]
    public string? Drivetrain { get; set; }
    /// <summary>"LHD" or "RHD".</summary>
    [MaxLength(3)]
    public string? SteeringSide { get; set; }
    [MaxLength(30)]
    public string? Market { get; set; }
    [MaxLength(200)]
    public string? Note { get; set; }
}

/// <summary>OEM or aftermarket / interchange part number, stored as typed and normalised.</summary>
[Table("PartNumbers")]
public class PartNumber
{
    public int Id { get; set; }
    [MaxLength(36)]
    public required string ItemId { get; set; }
    [MaxLength(60)]
    public required string Value { get; set; }
    [MaxLength(60)]
    public required string Normalized { get; set; }
    /// <summary>"OEM" or "Aftermarket".</summary>
    [MaxLength(20)]
    public string Kind { get; set; } = "OEM";
}

/// <summary>A size/spec value of a size-based part (tyre width = 265 ...).</summary>
[Table("PartSpecs")]
public class PartSpec
{
    public int Id { get; set; }
    [MaxLength(36)]
    public required string ItemId { get; set; }
    [MaxLength(40)]
    public required string Key { get; set; }
    [MaxLength(60)]
    public required string Value { get; set; }
}

/// <summary>Whole-vehicle details (identity, condition, papers). The VIN is never shown in full publicly.</summary>
[Table("VehicleDetails")]
public class VehicleDetails
{
    [Key, MaxLength(36)]
    public required string ItemId { get; set; }

    [MaxLength(60)]
    public string? Trim { get; set; }
    [MaxLength(30)]
    public string? BodyType { get; set; }
    [MaxLength(20)]
    public string? Fuel { get; set; }
    [MaxLength(20)]
    public string? EngineSize { get; set; }
    [MaxLength(20)]
    public string? Transmission { get; set; }
    [MaxLength(20)]
    public string? Drivetrain { get; set; }
    [MaxLength(3)]
    public string? SteeringSide { get; set; }
    /// <summary>Private: only the seller sees it; everyone else sees the last 6 characters.</summary>
    [MaxLength(20)]
    public string? Vin { get; set; }

    /// <summary>RunsAndDrives, RunsNotDrivable, DoesNotRun.</summary>
    [MaxLength(20)]
    public string? RunningStatus { get; set; }
    /// <summary>"km" or "mi" (from the country).</summary>
    [MaxLength(2)]
    public string MileageUnit { get; set; } = "km";
    /// <summary>None, Accident, Flood, Fire, Mechanical.</summary>
    [MaxLength(20)]
    public string? DamageType { get; set; }
    public string[] DamageAreas { get; set; } = [];
    public int? Keys { get; set; }
    public int? Owners { get; set; }
    [MaxLength(30)]
    public string? ServiceHistory { get; set; }
    public bool InspectionAllowed { get; set; }

    /// <summary>Papers by country (India: RC, NOC, insurance; USA: title; Nigeria: customs ...), key → value.</summary>
    [Column(TypeName = "jsonb")]
    public string PapersJson { get; set; } = "{}";

    [NotMapped]
    public Dictionary<string, string> Papers
    {
        get => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(PapersJson) ?? [];
        set => PapersJson = System.Text.Json.JsonSerializer.Serialize(value);
    }
}

/// <summary>
/// Order tracking steps. Which ones apply depends on the delivery method (see <c>Services/OrderTracking</c>):
/// Pickup: Sold → Ready for pickup → Picked up → Completed; Shipping: Sold → Shipped → Delivered → Completed.
/// Stored as numbers: removed values (3 Packed, 5 Out for delivery) were migrated and are never reused.
/// </summary>
public enum OrderStatus
{
    /// <summary>Sold – awaiting the next step.</summary>
    SoldAwaitingHandover = 0,
    /// <summary>Pickup: the buyer collected it (marked by the seller). Was "handed to the buyer's courier".</summary>
    PickedUp = 1,
    /// <summary>Confirmed by the buyer – the order is done.</summary>
    Completed = 2,
    /// <summary>Shipping: sent (courier and tracking number optional). Was "Dispatched".</summary>
    Shipped = 4,
    ReadyForPickup = 6,
    /// <summary>Shipping: the buyer confirmed it arrived.</summary>
    Delivered = 7
}

/// <summary>One step of an order's tracking, with when it happened and who did it.</summary>
[Table("OrderEvents")]
public class OrderEvent
{
    public int Id { get; set; }
    [MaxLength(36)]
    public required string OrderId { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    [MaxLength(100)]
    public required string By { get; set; }
    /// <summary>e.g. "Delhivery · AWB 1234" when dispatched.</summary>
    [MaxLength(200)]
    public string? Note { get; set; }
}

/// <summary>
/// A sale: created when an auction is Sold, or for each purchase of a fixed-price listing (several per listing when
/// it has more than one unit). No online payment in this phase.
/// </summary>
[Table("Orders")]
public class Order
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [MaxLength(36)]
    public required string AuctionId { get; set; }
    [MaxLength(100)]
    public required string Seller { get; set; }
    [MaxLength(100)]
    public required string Buyer { get; set; }
    /// <summary>Units bought (always 1 for auctions).</summary>
    public int Quantity { get; set; } = 1;
    /// <summary>Won in an auction or bought at a fixed price.</summary>
    public SaleType SaleType { get; set; }
    /// <summary>The item price: the winning total (part price + winning bid) or price × quantity. Shipping is extra.</summary>
    public int FinalTotal { get; set; }
    /// <summary>The listing's part price (× quantity): FinalTotal − PartPrice is the winning bid.</summary>
    public int PartPrice { get; set; }
    /// <summary>The listing's shipping fee (0 = free) when it offered shipping; added to the total only for Shipping.</summary>
    public int? ShippingFee { get; set; }
    /// <summary>The listing's country: shipping goes to addresses in this country only.</summary>
    [MaxLength(2)]
    public string? CountryCode { get; set; }
    /// <summary>The seller's exact pickup address (only the buyer of this order sees it).</summary>
    [MaxLength(300)]
    public string? PickupAddress { get; set; }
    [MaxLength(3)]
    public string? Currency { get; set; }
    [MaxLength(250)]
    public required string Title { get; set; }
    [MaxLength(500)]
    public string? ImageUrl { get; set; }
    [MaxLength(120)]
    public string? LocationName { get; set; }
    public DeliveryOptions DeliveryOptions { get; set; }
    /// <summary>The one delivery option used for this sale (set at once when the listing offered only one, else chosen by the buyer).</summary>
    public DeliveryOptions? DeliveryMethod { get; set; }
    /// <summary>Where the seller ships to, as one line (older orders have only this).</summary>
    [MaxLength(300)]
    public string? DeliveryAddress { get; set; }
    /// <summary>Shipping: the buyer's address (copied, so editing the address book doesn't change the order).</summary>
    public AddressInfo? ShipTo { get; set; }
    [MaxLength(60)]
    public string? Courier { get; set; }
    [MaxLength(60)]
    public string? TrackingNumber { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? HandedOverAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<OrderEvent> Events { get; set; } = [];

    // Contact details, shown only to the other party of this sale (captured from each party's sign-in token).
    [MaxLength(200)]
    public string? SellerEmail { get; set; }
    [MaxLength(30)]
    public string? SellerPhone { get; set; }
    [MaxLength(200)]
    public string? BuyerEmail { get; set; }
    [MaxLength(30)]
    public string? BuyerPhone { get; set; }
}

/// <summary>A buyer's saved vehicle ("My Garage").</summary>
[Table("GarageVehicles")]
public class GarageVehicle
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [MaxLength(100)]
    public required string User { get; set; }
    public int Year { get; set; }
    [MaxLength(60)]
    public required string Make { get; set; }
    [MaxLength(60)]
    public required string Model { get; set; }
    [MaxLength(60)]
    public string? Trim { get; set; }
    [MaxLength(60)]
    public string? Engine { get; set; }
    [MaxLength(40)]
    public string? Nickname { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A photo uploaded for a chat message or a review (not a listing photo): stored like listing photos (re-encoded, no
/// EXIF, with a thumbnail). The owner is recorded so a review can only use its writer's own photos.
/// </summary>
[Table("MediaFiles")]
public class MediaFile
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [MaxLength(100)]
    public required string Owner { get; set; }
    /// <summary>chat or review.</summary>
    [MaxLength(20)]
    public required string Purpose { get; set; }
    [MaxLength(500)]
    public required string Url { get; set; }
    [MaxLength(200)]
    public required string FileName { get; set; }
    [MaxLength(500)]
    public string? ThumbnailUrl { get; set; }
    [MaxLength(200)]
    public string? ThumbnailFileName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A copy of ChatService's blocks (UserBlocked / UserUnblocked): Blocked can't buy Blocker's listings.</summary>
[Table("UserBlocks")]
public class UserBlock
{
    [MaxLength(100)]
    public required string Blocker { get; set; }
    [MaxLength(100)]
    public required string Blocked { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A postal address for shipping: who receives it, how to reach them and where.</summary>
[Microsoft.EntityFrameworkCore.Owned]
public class AddressInfo
{
    [MaxLength(100)]
    public string Name { get; set; } = "";
    [MaxLength(30)]
    public string Phone { get; set; } = "";
    [MaxLength(150)]
    public string Line1 { get; set; } = "";
    [MaxLength(150)]
    public string? Line2 { get; set; }
    [MaxLength(80)]
    public string City { get; set; } = "";
    /// <summary>Postcode / PIN / ZIP / Eircode.</summary>
    [MaxLength(20)]
    public string Postcode { get; set; } = "";
    /// <summary>IN, US, NG or IE.</summary>
    [MaxLength(2)]
    public string CountryCode { get; set; } = "";

    /// <summary>"Ravi Kumar, +91 98…, 12 MG Road, Kukatpally, Hyderabad 500072, IN".</summary>
    public string OneLine() =>
        string.Join(", ", new[] { Name, Phone, Line1, Line2, $"{City} {Postcode}".Trim(), CountryCode }.Where(p => !string.IsNullOrWhiteSpace(p)));
}

/// <summary>A buyer's saved shipping address ("address book"); one of them is the default.</summary>
[Table("ShippingAddresses")]
public class ShippingAddress
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [MaxLength(100)]
    public required string User { get; set; }
    public AddressInfo Address { get; set; } = new();
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
