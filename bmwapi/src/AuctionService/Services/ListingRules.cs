using System.Globalization;
using System.Text.RegularExpressions;
using AuctionService.DTOs;
using AuctionService.Entities;
using Contracts;

namespace AuctionService.Services;

/// <summary>
/// The listing rules from the design doc (§4–§6, §9): applying a seller's changes, the generated title and the
/// checks before publishing. Pure functions (the part type must be loaded on the item).
/// </summary>
public static partial class ListingRules
{
    public const int MaxFitmentRows = 50;
    public const int MaxYearSpan = 30;
    public const int MinPartPhotos = 3;
    public const int MaxPartPhotos = 10;
    public const int MinVehiclePhotos = 8;
    public const int MaxVehiclePhotos = 20;
    public static readonly int[] Durations = [1, 3, 5, 7, 10];
    /// <summary>A seller-chosen auction end must be at least this far ahead when publishing …</summary>
    public static readonly TimeSpan MinAuctionLength = TimeSpan.FromHours(1);
    /// <summary>… and at most this far.</summary>
    public static readonly TimeSpan MaxAuctionLength = TimeSpan.FromDays(30);
    /// <summary>Fixed-price listings have no end: they stay Live until sold out or cancelled.</summary>
    public static readonly DateTime NoEnd = new(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Guided vehicle photos, in order (the first 8 are required).</summary>
    public static readonly string[] VehicleAngles = ["Front", "Rear", "Left", "Right", "Interior", "Dashboard", "EngineBay", "Damage"];

    /// <summary>Papers asked per country (all required for that country). Values are free text / yes-no.</summary>
    public static readonly Dictionary<string, string[]> PapersByCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IN"] = ["RcAvailable", "Noc", "InsuranceValidUntil"],
        ["US"] = ["TitleStatus"],
        ["NG"] = ["CustomsPapers", "Registration", "UsedType"],
        ["IE"] = ["RegistrationCertificate", "NctValidUntil"],
    };

    public static readonly string[] Fuels = ["Petrol", "Diesel", "CNG", "LPG", "Hybrid", "Electric"];
    public static readonly string[] Transmissions = ["Manual", "Automatic", "CVT", "DCT", "AMT"];
    public static readonly string[] Drivetrains = ["FWD", "RWD", "4WD", "AWD"];
    public static readonly string[] RunningStatuses = ["RunsAndDrives", "RunsNotDrivable", "DoesNotRun"];
    public static readonly string[] DamageTypes = ["None", "Accident", "Flood", "Fire", "Mechanical"];
    public static readonly string[] DamageAreaValues = ["Front", "Rear", "Left", "Right", "Roof", "Underbody"];

    public static int MaxPhotos(Item item) =>
        item.Kind == ListingKind.Vehicle || item.Kind is null && item.ItemType == ItemType.Vehicle ? MaxVehiclePhotos : MaxPartPhotos;

    // ── applying changes ───────────────────────────────────────────────

    /// <summary>Copies the sent fields onto the listing (nothing is validated here except shapes).</summary>
    public static void Apply(SaveListingDto dto, Auction auction, PartType? partType)
    {
        var item = auction.Item;

        if (dto.PartTypeId is not null && item.Kind != ListingKind.Vehicle)
        {
            item.PartTypeId = dto.PartTypeId;
            item.PartType = partType;
        }
        if (dto.TitleExtra is not null) item.TitleExtra = Clean(dto.TitleExtra, 80);
        if (dto.Description is not null) item.Description = Clean(dto.Description, 2000) ?? "";
        if (dto.Year is not null) item.Year = dto.Year.Value;
        if (dto.Make is not null) item.Make = Clean(dto.Make, 100) ?? "";
        if (dto.Model is not null) item.Model = Clean(dto.Model, 100) ?? "";
        if (dto.Colour is not null) item.Color = Clean(dto.Colour, 100);
        if (dto.Mileage is not null) item.Mileage = dto.Mileage;

        if (dto.PartDetails is not null && item.Kind != ListingKind.Vehicle) ApplyPartDetails(dto.PartDetails, item);

        if (dto.Fitments is not null)
        {
            item.Fitments.Clear();
            var order = 0;
            foreach (var f in dto.Fitments)
            {
                item.Fitments.Add(new PartFitment
                {
                    ItemId = item.Id,
                    SortOrder = order++,
                    YearFrom = f.YearFrom,
                    YearTo = f.YearTo == 0 ? f.YearFrom : f.YearTo,
                    MakeId = f.MakeId,
                    Make = Clean(f.Make, 60) ?? "",
                    ModelId = f.ModelId,
                    Model = Clean(f.Model, 60) ?? "",
                    Trim = Any(f.Trim, 60),
                    Engine = Any(f.Engine, 60),
                    Fuel = Any(f.Fuel, 20),
                    Transmission = Any(f.Transmission, 20),
                    Drivetrain = Any(f.Drivetrain, 20),
                    SteeringSide = Any(f.SteeringSide, 3)?.ToUpperInvariant(),
                    Market = Any(f.Market, 30),
                    Note = Clean(f.Note, 200)
                });
            }
        }

        if (dto.PartNumbers is not null)
        {
            item.PartNumbers.Clear();
            foreach (var n in dto.PartNumbers)
            {
                var value = Clean(n.Value, 60);
                var normalized = PartNumbers.Normalize(value);
                if (value is null || normalized is null) continue;
                if (item.PartNumbers.Any(x => x.Normalized == normalized)) continue;
                item.PartNumbers.Add(new PartNumber
                {
                    ItemId = item.Id,
                    Value = value,
                    Normalized = normalized,
                    Kind = string.Equals(n.Kind, "Aftermarket", StringComparison.OrdinalIgnoreCase) ? "Aftermarket" : "OEM"
                });
            }
        }

        if (dto.Specs is not null)
        {
            item.Specs.Clear();
            foreach (var (key, value) in dto.Specs)
            {
                var k = Clean(key, 40);
                var v = Clean(value, 60);
                if (k is null || v is null) continue;
                item.Specs.Add(new PartSpec { ItemId = item.Id, Key = k, Value = v });
            }
        }

        if (dto.Vehicle is not null && item.Kind == ListingKind.Vehicle) ApplyVehicle(dto.Vehicle, item);

        if (dto.Latitude is not null || dto.Longitude is not null)
        {
            item.Latitude = dto.Latitude;
            item.Longitude = dto.Longitude;
        }
        if (dto.LocationName is not null) item.LocationName = Clean(dto.LocationName, 120);
        if (dto.CountryCode is not null && RegionRules.IsSupported(dto.CountryCode))
        {
            item.CountryCode = dto.CountryCode.ToUpperInvariant();
            item.Currency = RegionRules.CurrencyFor(item.CountryCode);
            if (item.VehicleDetails is not null) item.VehicleDetails.MileageUnit = RegionRules.DistanceUnit(item.CountryCode);
        }
        if (dto.DeliveryOptions is not null)
        {
            var options = DeliveryOptions.None;
            foreach (var o in dto.DeliveryOptions)
                if (OrderTracking.ParseMethod(o) is { } parsed) options |= parsed;
            item.DeliveryOptions = options;
        }
        if (dto.SizeClass is not null)
            item.SizeClass = Enum.TryParse<SizeClass>(dto.SizeClass, true, out var size) ? size : null;
        if (dto.FreeShipping == true) item.ShippingFee = 0;
        else if (dto.FreeShipping == false && item.ShippingFee == 0) item.ShippingFee = null;
        if (dto.ShippingFee is not null && dto.FreeShipping != true) item.ShippingFee = dto.ShippingFee;
        if (dto.PickupAddress is not null) item.PickupAddress = Clean(dto.PickupAddress, 300);
        // Shipping details only exist while Shipping is offered (the pickup address only with Pickup).
        if (!item.DeliveryOptions.HasFlag(DeliveryOptions.Shipping))
        {
            item.ShippingFee = null;
            item.SizeClass = null;
        }
        if (!item.DeliveryOptions.HasFlag(DeliveryOptions.Pickup)) item.PickupAddress = null;

        if (dto.PartPrice is not null)
        {
            // No reserve in this phase: reserve = part price. For fixed-price listings it is the price.
            auction.StartingPrice = dto.PartPrice.Value;
            auction.ReservePrice = dto.PartPrice.Value;
        }
        if (dto.SaleType is not null && Enum.TryParse<SaleType>(dto.SaleType, true, out var saleType) && Enum.IsDefined(saleType))
            auction.SaleType = saleType;
        if (dto.DurationDays is not null) item.DurationDays = dto.DurationDays;
        if (dto.AuctionEnd is not null)
        {
            // A chosen date and time replaces the older "days from publishing".
            auction.AuctionEnd = AsUtc(dto.AuctionEnd.Value);
            item.DurationDays = null;
        }

        SyncDerivedFields(item);
    }

    /// <summary>Times without a zone are taken as UTC (the app always sends UTC).</summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    /// <summary>When a listing being published ends: never (fixed price), N days from now (older apps) or the chosen time.</summary>
    public static DateTime EndWhenPublished(Auction auction, DateTime now) =>
        auction.SaleType == SaleType.FixedPrice ? NoEnd
        : auction.Item.DurationDays is { } days ? now.AddDays(days)
        : auction.AuctionEnd;

    private static void ApplyPartDetails(PartDetailsDto dto, Item item)
    {
        var d = item.PartDetails ??= new PartDetails { ItemId = item.Id };
        d.BrandType = Enum.TryParse<BrandType>(dto.BrandType, true, out var bt) ? bt : null;
        d.Brand = Clean(dto.Brand, 60);
        d.Position = Pick(dto.Position, "Front", "Rear");
        d.Side = Pick(dto.Side, "Left", "Right", "Centre");
        d.UsedGrade = Enum.TryParse<UsedGrade>(dto.UsedGrade, true, out var g) ? g : null;
        d.DonorMileage = dto.DonorMileage is >= 0 ? dto.DonorMileage : null;
        d.Colour = Clean(dto.Colour, 40);
        d.PaintCode = Clean(dto.PaintCode, 40);
        d.Includes = Clean(dto.Includes, 300);
        d.Warranty = Clean(dto.Warranty, 300);
        item.Condition = Enum.TryParse<ItemCondition>(dto.Condition, true, out var c) ? c : null;
        if (item.Condition != ItemCondition.Used) d.UsedGrade = null;
        item.Quantity = dto.Quantity ?? item.Quantity ?? 1;
    }

    private static void ApplyVehicle(VehicleDetailsDto dto, Item item)
    {
        var v = item.VehicleDetails ??= new VehicleDetails { ItemId = item.Id };
        v.Trim = Any(dto.Trim, 60);
        v.BodyType = Clean(dto.BodyType, 30);
        v.Fuel = Clean(dto.Fuel, 20);
        v.EngineSize = Clean(dto.EngineSize, 20);
        v.Transmission = Clean(dto.Transmission, 20);
        v.Drivetrain = Clean(dto.Drivetrain, 20);
        v.SteeringSide = Clean(dto.SteeringSide, 3)?.ToUpperInvariant();
        // The masked value the app shows ("••••••123456") must never overwrite the real VIN.
        if (dto.Vin is not null && !dto.Vin.Contains('•'))
            v.Vin = Clean(dto.Vin.Replace(" ", ""), 20)?.ToUpperInvariant();
        v.RunningStatus = Clean(dto.RunningStatus, 20);
        v.MileageUnit = RegionRules.DistanceUnit(item.CountryCode);
        v.DamageType = Clean(dto.DamageType, 20);
        v.DamageAreas = dto.DamageAreas.Where(a => DamageAreaValues.Contains(a)).Distinct().ToArray();
        v.Keys = dto.Keys;
        v.Owners = dto.Owners;
        v.ServiceHistory = Clean(dto.ServiceHistory, 30);
        v.InspectionAllowed = dto.InspectionAllowed;
        v.Papers = dto.Papers
            .Where(p => !string.IsNullOrWhiteSpace(p.Key) && !string.IsNullOrWhiteSpace(p.Value))
            .ToDictionary(p => p.Key.Trim(), p => p.Value.Trim().Length > 60 ? p.Value.Trim()[..60] : p.Value.Trim());
    }

    /// <summary>
    /// Keeps the older item fields filled (search, custom orders and older clients read them): fitment mode,
    /// part name / category, the first fitment as make/model/year, the first part number, and the title.
    /// </summary>
    public static void SyncDerivedFields(Item item)
    {
        if (item.Kind == ListingKind.Vehicle)
        {
            item.ItemType = ItemType.Vehicle;
            item.FitmentMode = null;
        }
        else
        {
            item.ItemType = ItemType.Part;
            item.FitmentMode = item.Kind == ListingKind.VehicleSpecificPart
                ? Entities.FitmentMode.VehicleSpecific
                : item.PartType?.FitmentMode == Entities.FitmentMode.SizeBased ? Entities.FitmentMode.SizeBased : Entities.FitmentMode.Universal;
            item.PartName = item.PartType?.Name ?? item.PartName;
            item.PartCategory = item.PartType?.Category is { } category
                ? (category.Parent ?? category).LegacyCategory
                : item.PartCategory;
            item.PartNumber = item.PartNumbers.FirstOrDefault()?.Value;
            item.Quantity ??= 1;

            var first = item.Fitments.OrderBy(f => f.SortOrder).FirstOrDefault();
            if (item.FitmentMode == Entities.FitmentMode.VehicleSpecific && first is not null)
            {
                item.Make = first.Make;
                item.Model = first.Model;
                item.Year = first.YearFrom;
                item.YearTo = first.YearTo;
            }
            else if (item.FitmentMode != Entities.FitmentMode.VehicleSpecific)
            {
                // Common parts fit any vehicle.
                item.Make = "";
                item.Model = "";
                item.Year = 0;
                item.YearTo = null;
                item.Fitments.Clear();
            }
            if (item.FitmentMode == Entities.FitmentMode.Universal) item.Specs.Clear();
        }

        item.Title = Title(item);
    }

    // ── title ──────────────────────────────────────────────────────────

    /// <summary>
    /// "2016–2020 Toyota Hilux Front Bumper – Genuine OEM – Used (Grade A)", "Tyre 265/65 R17 – Bridgestone – New",
    /// "Dash Cam – Aftermarket – New", "2018 Toyota Hilux SR5 – Diesel Manual – 85,000 km". The seller's extra
    /// words are appended; the generated part can't be removed.
    /// </summary>
    public static string Title(Item item)
    {
        var generated = GeneratedTitle(item);
        var extra = item.TitleExtra?.Trim();
        var title = string.IsNullOrEmpty(extra) ? generated : $"{generated} – {extra}";
        return title.Length > 250 ? title[..250] : title;
    }

    public static string GeneratedTitle(Item item)
    {
        if (item.Kind == ListingKind.Vehicle)
        {
            var v = item.VehicleDetails;
            var head = Join(" ", item.Year > 0 ? item.Year.ToString(CultureInfo.InvariantCulture) : null, item.Make, item.Model, v?.Trim);
            var drive = Join(" ", v?.Fuel, v?.Transmission);
            var miles = item.Mileage is null ? null
                : $"{item.Mileage.Value.ToString("N0", CultureInfo.InvariantCulture)} {v?.MileageUnit ?? "km"}";
            return Join(" – ", head, drive, miles);
        }

        var d = item.PartDetails;
        var partName = TitleCase(item.PartType?.Name ?? item.PartName ?? "Part");
        var position = Join(" ", d?.Position, d?.Side is "Centre" ? null : d?.Side);
        var part = Join(" ", position, partName);
        var condition = item.Condition switch
        {
            ItemCondition.Used => d?.UsedGrade is { } grade ? $"Used (Grade {grade})" : "Used",
            ItemCondition.ForRepair => "For repair",
            { } c => c.ToString(),
            null => null
        };
        var brand = d?.BrandType switch
        {
            Entities.BrandType.GenuineOem => "Genuine OEM",
            Entities.BrandType.OemEquivalent => Join(" ", d.Brand, "(OEM-equivalent)"),
            Entities.BrandType.Aftermarket => string.IsNullOrWhiteSpace(d.Brand) ? "Aftermarket" : d.Brand,
            _ => null
        };

        if (item.FitmentMode == Entities.FitmentMode.VehicleSpecific && item.Fitments.Count > 0)
        {
            var f = item.Fitments.OrderBy(x => x.SortOrder).First();
            var vehicle = Join(" ", YearRange(f.YearFrom, f.YearTo), f.Make, f.Model);
            if (item.Fitments.Count > 1) vehicle += " & more";
            return Join(" – ", Join(" ", vehicle, part), brand, condition);
        }

        if (item.FitmentMode == Entities.FitmentMode.SizeBased)
            return Join(" – ", Join(" ", part, SpecSummary(item)), brand, condition);

        return Join(" – ", part, brand, condition);
    }

    public static string YearRange(int from, int to) =>
        to > from ? $"{from}–{to}" : from.ToString(CultureInfo.InvariantCulture);

    /// <summary>"Fits Toyota Hilux 2016–2020 (+2 more)", "Universal – fits any vehicle", "Size 265/65 R17".</summary>
    public static string? FitmentSummary(Item item)
    {
        if (item.Kind == ListingKind.Vehicle) return null;
        return item.FitmentMode switch
        {
            Entities.FitmentMode.VehicleSpecific when item.Fitments.Count > 0 =>
                $"Fits {item.Fitments.OrderBy(x => x.SortOrder).Select(f => $"{f.Make} {f.Model} {YearRange(f.YearFrom, f.YearTo)}").First()}"
                + (item.Fitments.Count > 1 ? $" (+{item.Fitments.Count - 1} more)" : ""),
            Entities.FitmentMode.SizeBased => SpecSummary(item) is { } s ? $"Size {s} – fits any vehicle that takes it" : "Fits by size",
            Entities.FitmentMode.Universal => "Universal – fits any vehicle",
            _ => item.Make.Length > 0 ? $"Fits {item.Make} {item.Model} {YearRange(item.Year, item.YearTo ?? item.Year)}" : null
        };
    }

    /// <summary>Tyre "265/65 R17", otherwise the spec values in the part type's order ("H4 60/55W").</summary>
    public static string? SpecSummary(Item item)
    {
        if (item.Specs.Count == 0) return null;
        var specs = item.Specs.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        if (specs.TryGetValue("width", out var w) && specs.TryGetValue("profile", out var p) && specs.TryGetValue("rim", out var r))
            return $"{w}/{p} R{r.TrimStart('R', 'r')}";
        var order = item.PartType?.SpecFields.Select(f => f.Key).ToList() ?? [];
        return string.Join(" ", item.Specs
            .OrderBy(s => order.IndexOf(s.Key) is var i && i < 0 ? int.MaxValue : i)
            .Select(s => s.Value + (item.PartType?.SpecFields.FirstOrDefault(f => f.Key == s.Key)?.Unit ?? "")));
    }

    // ── validation ─────────────────────────────────────────────────────

    /// <summary>Everything a listing needs before it is published (field → messages).</summary>
    public static Dictionary<string, string[]> ValidateForPublish(Auction auction, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
            list.Add(message);
        }

        var item = auction.Item;
        var maxYear = DateTime.UtcNow.Year + 1;

        if (item.Kind is null) Add("kind", "Choose what you are adding");

        if (item.Kind == ListingKind.Vehicle)
        {
            var v = item.VehicleDetails;
            if (item.Year < 1950 || item.Year > maxYear) Add("year", $"Year must be between 1950 and {maxYear}");
            if (string.IsNullOrWhiteSpace(item.Make)) Add("make", "Choose the make");
            if (string.IsNullOrWhiteSpace(item.Model)) Add("model", "Choose the model");
            if (string.IsNullOrWhiteSpace(v?.Fuel)) Add("fuel", "Choose the fuel");
            if (string.IsNullOrWhiteSpace(v?.Transmission)) Add("transmission", "Choose the transmission");
            if (string.IsNullOrWhiteSpace(v?.RunningStatus)) Add("runningStatus", "Choose the running status");
            if (item.Mileage is null or < 0) Add("mileage", "Enter the mileage");
            if (string.IsNullOrWhiteSpace(v?.DamageType)) Add("damageType", "Choose the damage type (or None)");
            if (v?.Vin is { Length: > 0 } vin && (vin.Length < 6 || vin.Length > 17)) Add("vin", "VIN / chassis number must be 6–17 characters");
            if (item.CountryCode is { } country && PapersByCountry.TryGetValue(country, out var papers))
                foreach (var paper in papers)
                    if (v is null || !v.Papers.TryGetValue(paper, out var value) || string.IsNullOrWhiteSpace(value))
                        Add("papers", $"Fill in the papers: {paper}");
        }
        else if (item.Kind is not null)
        {
            var type = item.PartType;
            if (type is null) Add("partTypeId", "Choose the part type");

            if (item.Kind == ListingKind.CommonPart && type?.FitmentMode == Entities.FitmentMode.VehicleSpecific)
                Add("partTypeId", "This part type fits specific vehicles – add it as a vehicle-specific part");

            if (item.FitmentMode == Entities.FitmentMode.VehicleSpecific) ValidateFitments(item, Add);

            if (item.FitmentMode == Entities.FitmentMode.SizeBased && type is not null)
            {
                foreach (var field in type.SpecFields.Where(f => f.Required))
                    if (!item.Specs.Any(s => s.Key == field.Key && !string.IsNullOrWhiteSpace(s.Value)))
                        Add("specs", $"{field.Label} is required");
            }

            var d = item.PartDetails;
            if (d?.BrandType is null) Add("brandType", "Choose the brand type");
            else if (d.BrandType != Entities.BrandType.GenuineOem && string.IsNullOrWhiteSpace(d.Brand))
                Add("brand", "Enter the brand");
            if (item.Condition is null) Add("condition", "Choose the condition");
            if (item.Condition == ItemCondition.Used && d?.UsedGrade is null) Add("usedGrade", "Choose the used grade (A, B or C)");
            if (type?.HasPosition == true && d?.Position is null) Add("position", "Choose Front or Rear");
            if (type?.HasSide == true && d?.Side is null) Add("side", "Choose Left, Right or Centre (as seen from the driver's seat)");
            if (item.Quantity is null or < 1) Add("quantity", "Quantity must be at least 1");
        }

        // Photos
        var photos = item.Images.Count;
        var (min, max) = item.Kind == ListingKind.Vehicle ? (MinVehiclePhotos, MaxVehiclePhotos) : (MinPartPhotos, MaxPartPhotos);
        if (photos < min) Add("photos", $"Add at least {min} photos ({photos} so far)");
        if (photos > max) Add("photos", $"At most {max} photos");

        // Location & delivery
        if (item.Latitude is null || item.Longitude is null ||
            !GeoMath.IsValidCoordinate(item.Latitude.Value, item.Longitude.Value))
            Add("location", "Set the item location");
        if (item.CountryCode is null) Add("countryCode", "The listing country is missing");
        if (item.DeliveryOptions == DeliveryOptions.None) Add("deliveryOptions", "Choose Pickup, Shipping or both");
        if (item.DeliveryOptions.HasFlag(DeliveryOptions.Shipping))
        {
            if (item.ShippingFee is null) Add("shippingFee", "Enter the shipping fee or choose Free shipping");
            else if (item.ShippingFee < 0) Add("shippingFee", "The shipping fee can't be negative");
            if (item.SizeClass is null) Add("sizeClass", "Choose the size class for shipping");
        }
        if (ContainsContactDetails(item.PickupAddress))
            Add("pickupAddress", "Please don't put phone numbers or e-mail addresses in the pickup address");

        // Auction / fixed price
        if (auction.SaleType == SaleType.FixedPrice)
        {
            if (auction.StartingPrice <= 0) Add("partPrice", "Enter the price");
            if (auction.QuantitySold > 0 && auction.QuantityListed <= auction.QuantitySold)
                Add("quantity", $"Quantity must be more than the {auction.QuantitySold} already sold");
        }
        else
        {
            if (auction.StartingPrice <= 0) Add("partPrice", "Enter the part price");
            if (item.DurationDays is { } days)
            {
                if (!Durations.Contains(days)) Add("durationDays", "Choose 1, 3, 5, 7 or 10 days");
            }
            else
            {
                // A draft must run at least an hour; a Live listing being edited only has to still be running.
                var earliest = auction.Status == Status.Draft ? now + MinAuctionLength : now;
                if (auction.AuctionEnd < earliest)
                    Add("auctionEnd", "Choose an end date and time at least 1 hour from now");
                else if (auction.AuctionEnd > now + MaxAuctionLength)
                    Add("auctionEnd", $"An auction can run for at most {MaxAuctionLength.TotalDays:0} days");
            }
        }

        // No phone numbers / e-mail addresses in the title or description.
        if (ContainsContactDetails(item.TitleExtra) || ContainsContactDetails(item.Description))
            Add("description", "Please don't put phone numbers or e-mail addresses in the title or description");

        return errors.ToDictionary(x => x.Key, x => x.Value.ToArray());
    }

    private static void ValidateFitments(Item item, Action<string, string> add)
    {
        var maxYear = DateTime.UtcNow.Year + 1;
        if (item.Fitments.Count == 0) add("fitments", "Add at least one vehicle this part fits");
        if (item.Fitments.Count > MaxFitmentRows) add("fitments", $"At most {MaxFitmentRows} vehicles");

        var seen = new HashSet<string>();
        foreach (var (f, i) in item.Fitments.OrderBy(x => x.SortOrder).Select((f, i) => (f, i + 1)))
        {
            if (string.IsNullOrWhiteSpace(f.Make) || string.IsNullOrWhiteSpace(f.Model))
                add("fitments", $"Vehicle {i}: choose the make and model");
            if (f.YearFrom < 1950 || f.YearTo > maxYear || f.YearFrom > f.YearTo)
                add("fitments", $"Vehicle {i}: years must be between 1950 and {maxYear}, 'from' before 'to'");
            else if (f.YearTo - f.YearFrom + 1 > MaxYearSpan)
                add("fitments", $"Vehicle {i}: a year range can cover at most {MaxYearSpan} years");
            if (f.SteeringSide is not null and not ("LHD" or "RHD"))
                add("fitments", $"Vehicle {i}: steering side must be LHD or RHD");

            var key = string.Join("|", f.YearFrom, f.YearTo, f.Make.ToLowerInvariant(), f.Model.ToLowerInvariant(),
                f.Trim?.ToLowerInvariant(), f.Engine?.ToLowerInvariant(), f.Fuel, f.Transmission, f.Drivetrain, f.SteeringSide, f.Market);
            if (!seen.Add(key)) add("fitments", $"Vehicle {i} is listed twice");
        }
    }

    public static bool ContainsContactDetails(string? text) =>
        !string.IsNullOrWhiteSpace(text) && (EmailRegex().IsMatch(text) || PhoneRegex().IsMatch(text));

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailRegex();

    // 9+ digits, optionally with spaces / dashes / a leading +, e.g. +234 803 123 4567 or 98765-43210.
    [GeneratedRegex(@"\+?\d[\d\s-]{8,}\d")]
    private static partial Regex PhoneRegex();

    // ── helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// The city of an item location for "Pickup in …": "Kukatpally, Hyderabad" → "Hyderabad", "Houston, TX" → "Houston"
    /// (a trailing state code is skipped), "Dublin" → "Dublin".
    /// </summary>
    public static string? PublicCity(string? locationName)
    {
        var parts = (locationName ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;
        var last = parts[^1];
        return parts.Length > 1 && last.Length <= 3 && last.All(char.IsUpper) ? parts[^2] : last;
    }

    public static string? Clean(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length > max ? v[..max] : v;
    }

    /// <summary>Empty or "Any" = not restricted.</summary>
    private static string? Any(string? value, int max)
    {
        var v = Clean(value, max);
        return v is null || v.Equals("any", StringComparison.OrdinalIgnoreCase) ? null : v;
    }

    private static string? Pick(string? value, params string[] allowed) =>
        allowed.FirstOrDefault(a => string.Equals(a, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string Join(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    /// <summary>"front bumper" → "Front Bumper" (keeps "LED", "ECU", "AC" ...).</summary>
    public static string TitleCase(string text) =>
        string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 1 && w.All(c => !char.IsLetter(c) || char.IsUpper(c)) ? w : char.ToUpperInvariant(w[0]) + w[1..]));

    /// <summary>"••••••" + last 6 (never the full VIN publicly).</summary>
    public static string? MaskVin(string? vin) =>
        string.IsNullOrEmpty(vin) ? null : vin.Length <= 6 ? vin : "••••••" + vin[^6..];
}
