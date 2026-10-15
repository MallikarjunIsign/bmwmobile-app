using AuctionService.DTOs;
using AuctionService.Entities;
using AuctionService.Services;
using Contracts;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace AuctionService.Data;

/// <summary>
/// Demo data for the listing model: converts the older demo items, and adds realistic listings of all three kinds
/// (common parts, vehicle-specific parts, vehicles) in India, the USA, Nigeria and Ireland, sold by ShopCarParts demo
/// sellers. Their photos are generated and stored through the configured image storage (LocalDiskStorage locally).
/// </summary>
public static class DemoListings
{
    private record Demo(
        string Id, string Seller, string Country, string Place, double Lat, double Lon, ListingKind Kind, string? PartType,
        int Price, SaveListingDto Details, double DurationHours = 0, int Days = 7, int FixedPriceUnits = 0);

    private static FitmentDto Fit(int from, int to, string make, string model, string? trim = null) =>
        new() { YearFrom = from, YearTo = to, Make = make, Model = model, Trim = trim };

    private static PartDetailsDto Part(string brandType, string condition, string? grade = null, string? brand = null,
        string? position = null, string? side = null, string? colour = null, string? includes = null, string? warranty = null) =>
        new()
        {
            BrandType = brandType, Condition = condition, UsedGrade = grade, Brand = brand, Position = position, Side = side,
            Colour = colour, Includes = includes, Warranty = warranty, Quantity = 1
        };

    private static List<Demo> All() =>
    [
        // ── India (Hyderabad) ──
        new("demo-in-swift-bonnet", "BidMyParts_Hyderabad", "IN", "Ameerpet, Hyderabad", 17.4375, 78.4483,
            ListingKind.VehicleSpecificPart, "Bonnet", 6500, new SaveListingDto
            {
                Description = "Genuine bonnet from a 2019 Swift. Pearl Arctic White, no dents, two small stone chips at the front edge.",
                Fitments = [Fit(2018, 2023, "Maruti Suzuki", "Swift")],
                PartNumbers = [new() { Value = "57300M52R00" }],
                PartDetails = Part("GenuineOem", "Used", "A", colour: "White (ZVR)"),
            }),
        new("demo-in-hilux-bumper", "BidMyParts_Hyderabad", "IN", "Kukatpally, Hyderabad", 17.4849, 78.4138,
            ListingKind.VehicleSpecificPart, "Bumper", 14000, new SaveListingDto
            {
                Description = "Front bumper cover removed from a 2018 Hilux. Light scuffs, all clips intact, ready for paint.",
                Fitments = [Fit(2016, 2020, "Toyota", "Hilux"), Fit(2016, 2020, "Toyota", "Fortuner")],
                PartNumbers = [new() { Value = "52119-0K935" }],
                PartDetails = Part("GenuineOem", "Used", "A", position: "Front", colour: "Super White 040"),
            }),
        new("demo-in-creta-headlamp", "BidMyParts_Hyderabad", "IN", "Secunderabad, Hyderabad", 17.4399, 78.4983,
            ListingKind.VehicleSpecificPart, "Headlamp", 18000, new SaveListingDto
            {
                Description = "New LED projector headlamp, left side (as seen from the driver's seat). Boxed.",
                Fitments = [Fit(2020, 2023, "Hyundai", "Creta")],
                PartNumbers = [new() { Value = "92101-I6000" }],
                PartDetails = Part("GenuineOem", "New", side: "Left", includes: "with bulbs and wiring"),
            }),
        new("demo-in-tyre-265-65-17", "BidMyParts_Hyderabad", "IN", "Madhapur, Hyderabad", 17.4483, 78.3915,
            ListingKind.CommonPart, "Tyre", 9800, new SaveListingDto
            {
                Description = "MRF Wanderer A/T, 2024 manufacture, unused. Fits any vehicle that takes 265/65 R17. Price per tyre.",
                Specs = new() { ["width"] = "265", ["profile"] = "65", ["rim"] = "17", ["loadIndex"] = "112", ["speedRating"] = "T" },
                PartDetails = Part("Aftermarket", "New", brand: "MRF"),
            }, FixedPriceUnits: 4),
        new("demo-in-dashcam", "BidMyParts_Hyderabad", "IN", "LB Nagar, Hyderabad", 17.3457, 78.5522,
            ListingKind.CommonPart, "Dash cam", 3500, new SaveListingDto
            {
                Description = "1440p dash cam with night vision and a 64 GB card. Universal windscreen mount.",
                PartDetails = Part("Aftermarket", "New", brand: "70mai", includes: "64 GB microSD card, USB cable"),
            }, DurationHours: 2),
        new("demo-in-swift-car", "BidMyParts_Hyderabad", "IN", "Kondapur, Hyderabad", 17.4600, 78.3640,
            ListingKind.Vehicle, null, 450000, new SaveListingDto
            {
                Year = 2019, Make = "Maruti Suzuki", Model = "Swift", Colour = "Red", Mileage = 42000,
                Description = "Single owner, company-serviced Swift VXi. Minor scratch on the rear bumper.",
                Vehicle = new VehicleDetailsDto
                {
                    Trim = "VXi", BodyType = "Hatchback", Fuel = "Petrol", EngineSize = "1.2", Transmission = "Manual",
                    Drivetrain = "FWD", SteeringSide = "RHD", Vin = "MA3EWDE1S00123456", RunningStatus = "RunsAndDrives",
                    DamageType = "None", Keys = 2, Owners = 1, ServiceHistory = "Full", InspectionAllowed = true,
                    Papers = new() { ["RcAvailable"] = "Yes", ["Noc"] = "Not needed", ["InsuranceValidUntil"] = "2027-03-31" }
                },
            }),

        // ── Nigeria (Lagos, Abuja) ──
        new("demo-ng-camry-bumper", "BidMyParts_Lagos", "NG", "Ikeja, Lagos", 6.6018, 3.3515,
            ListingKind.VehicleSpecificPart, "Bumper", 85000, new SaveListingDto
            {
                Description = "Front bumper for Camry 2012–2017, OEM-equivalent, fog lamp holes. Small crack on a lower tab.",
                Fitments = [Fit(2012, 2017, "Toyota", "Camry")],
                PartDetails = Part("OemEquivalent", "Used", "B", brand: "TYC", position: "Front", colour: "Unpainted"),
            }),
        new("demo-ng-corolla-gearbox", "BidMyParts_Lagos", "NG", "Lekki, Lagos", 6.4698, 3.5852,
            ListingKind.VehicleSpecificPart, "Gearbox", 350000, new SaveListingDto
            {
                Description = "Tokunbo automatic gearbox, tested before removal. 3-month warranty.",
                Fitments = [new FitmentDto { YearFrom = 2009, YearTo = 2013, Make = "Toyota", Model = "Corolla", Transmission = "Automatic", Engine = "1.8" }],
                PartNumbers = [new() { Value = "30500-12C10" }],
                PartDetails = Part("GenuineOem", "Used", "A", warranty: "3 months", includes: "torque converter"),
            }),
        new("demo-ng-battery", "BidMyParts_Lagos", "NG", "Wuse, Abuja", 9.0765, 7.3986,
            ListingKind.CommonPart, "Battery", 65000, new SaveListingDto
            {
                Description = "New 12V 75Ah maintenance-free battery, 12-month warranty.",
                Specs = new() { ["capacityAh"] = "75", ["voltage"] = "12V", ["terminalLayout"] = "Right +" },
                PartDetails = Part("Aftermarket", "New", brand: "Exide", warranty: "12 months"),
            }),
        new("demo-ng-corolla-car", "BidMyParts_Lagos", "NG", "Surulere, Lagos", 6.5000, 3.3500,
            ListingKind.Vehicle, null, 8500000, new SaveListingDto
            {
                Year = 2015, Make = "Toyota", Model = "Corolla", Colour = "Silver", Mileage = 98000,
                Description = "Foreign-used (Tokunbo) Corolla LE, cleared, with complete customs papers.",
                Vehicle = new VehicleDetailsDto
                {
                    Trim = "LE", BodyType = "Sedan", Fuel = "Petrol", EngineSize = "1.8", Transmission = "Automatic",
                    Drivetrain = "FWD", SteeringSide = "LHD", Vin = "2T1BURHE0FC123456", RunningStatus = "RunsAndDrives",
                    DamageType = "None", Keys = 1, Owners = 2, ServiceHistory = "Partial", InspectionAllowed = true,
                    Papers = new() { ["CustomsPapers"] = "Yes", ["Registration"] = "Yes", ["UsedType"] = "Foreign-used" }
                },
            }),

        // ── USA (Texas) ──
        new("demo-us-f150-headlamp", "BidMyParts_Texas", "US", "Houston, TX", 29.7604, -95.3698,
            ListingKind.VehicleSpecificPart, "Headlamp", 180, new SaveListingDto
            {
                Description = "New aftermarket halogen headlamp assembly, right side (as seen from the driver's seat).",
                Fitments = [Fit(2015, 2020, "Ford", "F-150")],
                PartNumbers = [new() { Value = "FL3Z-13008-E" }, new() { Value = "20-9600-00", Kind = "Aftermarket" }],
                PartDetails = Part("Aftermarket", "New", brand: "TYC", side: "Right"),
            }),
        new("demo-us-silverado-taillamp", "BidMyParts_Texas", "US", "Pasadena, TX", 29.6911, -95.2091,
            ListingKind.VehicleSpecificPart, "Tail lamp", 95, new SaveListingDto
            {
                Description = "OEM tail lamp, left side. No cracks, all bulbs work.",
                Fitments = [Fit(2014, 2018, "Chevrolet", "Silverado")],
                PartNumbers = [new() { Value = "84152765" }],
                PartDetails = Part("GenuineOem", "Used", "A", side: "Left"),
            }),
        new("demo-us-tyre-265-65-17", "BidMyParts_Texas", "US", "Dallas, TX", 32.7767, -96.7970,
            ListingKind.CommonPart, "Tyre", 160, new SaveListingDto
            {
                Description = "Bridgestone Dueler H/T 684 II, new, 2024.",
                Specs = new() { ["width"] = "265", ["profile"] = "65", ["rim"] = "17", ["loadIndex"] = "112", ["speedRating"] = "S" },
                PartDetails = Part("Aftermarket", "New", brand: "Bridgestone"),
            }),
        new("demo-us-civic-car", "BidMyParts_Texas", "US", "Sugar Land, TX", 29.6197, -95.6349,
            ListingKind.Vehicle, null, 13500, new SaveListingDto
            {
                Year = 2017, Make = "Honda", Model = "Civic", Colour = "Blue", Mileage = 61000,
                Description = "Clean-title Civic EX, new tires in 2025, minor door ding on the right.",
                Vehicle = new VehicleDetailsDto
                {
                    Trim = "EX", BodyType = "Sedan", Fuel = "Petrol", EngineSize = "2.0", Transmission = "CVT",
                    Drivetrain = "FWD", SteeringSide = "LHD", Vin = "19XFC2F72HE123456", RunningStatus = "RunsAndDrives",
                    DamageType = "None", Keys = 2, Owners = 2, ServiceHistory = "Full", InspectionAllowed = true,
                    Papers = new() { ["TitleStatus"] = "Clean" }
                },
            }),

        // ── Ireland (Dublin, Cork) ──
        new("demo-ie-golf-mirror", "BidMyParts_Dublin", "IE", "Dublin", 53.3498, -6.2603,
            ListingKind.VehicleSpecificPart, "Side mirror", 75, new SaveListingDto
            {
                Description = "Electric, heated door mirror, right side. Cover scuffed (grade B).",
                Fitments = [new FitmentDto { YearFrom = 2013, YearTo = 2019, Make = "Volkswagen", Model = "Golf", SteeringSide = "RHD" }],
                PartDetails = Part("GenuineOem", "Used", "B", side: "Right", colour: "Black"),
            }),
        new("demo-ie-wiper", "BidMyParts_Dublin", "IE", "Cork", 51.8985, -8.4756,
            ListingKind.CommonPart, "Wiper blade", 18, new SaveListingDto
            {
                Description = "Bosch Aerotwin 24\" flat wiper blade, new.",
                Specs = new() { ["length"] = "24" },
                PartDetails = Part("Aftermarket", "New", brand: "Bosch"),
            }),
        new("demo-ie-golf-car", "BidMyParts_Dublin", "IE", "Swords, Dublin", 53.4597, -6.2181,
            ListingKind.Vehicle, null, 9500, new SaveListingDto
            {
                Year = 2016, Make = "Volkswagen", Model = "Golf", Colour = "Grey", Mileage = 120000,
                Description = "Golf 1.6 TDI Highline, NCT until next year, full service history.",
                Vehicle = new VehicleDetailsDto
                {
                    Trim = "Highline", BodyType = "Hatchback", Fuel = "Diesel", EngineSize = "1.6", Transmission = "Manual",
                    Drivetrain = "FWD", SteeringSide = "RHD", Vin = "WVWZZZAUZGW123456", RunningStatus = "RunsAndDrives",
                    DamageType = "None", Keys = 2, Owners = 2, ServiceHistory = "Full", InspectionAllowed = true,
                    Papers = new() { ["RegistrationCertificate"] = "Yes", ["NctValidUntil"] = "2027-06-30" }
                },
            }),
    ];

    /// <summary>A typical domestic courier fee for a part, in the country's currency.</summary>
    private static readonly Dictionary<string, int> DemoShippingFees = new() { ["IN"] = 500, ["US"] = 25, ["NG"] = 5000, ["IE"] = 15 };

    private static readonly Dictionary<string, SKColor> Colours = new()
    {
        ["IN"] = new SKColor(0x7A, 0x1F, 0xD6), ["NG"] = new SKColor(0x0F, 0x76, 0x4E),
        ["US"] = new SKColor(0x1E, 0x4F, 0xB8), ["IE"] = new SKColor(0x0E, 0x7C, 0x7B),
    };

    /// <summary>Adds the demo listings that aren't there yet. Returns their ids (to publish AuctionCreated).</summary>
    public static async Task<List<string>> AddAsync(AuctionDbContext context, IServiceProvider services)
    {
        var demos = All();
        var ids = demos.Select(d => d.Id).ToList();

        // Demo listings added before the rename have no seller name, so buyers saw the old "BidMyParts_…" username.
        // Give them the brand name once; the new UpdatedAt makes the Search service re-index them when it starts.
        var unnamed = await context.Auctions
            .Where(a => ids.Contains(a.Id) && a.SellerName == null && a.Seller.StartsWith("BidMyParts_")).ToListAsync();
        foreach (var a in unnamed)
        {
            a.SellerName = a.Seller.Replace("BidMyParts_", "ShopCarParts ");
            a.UpdatedAt = DateTime.UtcNow;
        }
        if (unnamed.Count > 0) await context.SaveChangesAsync();

        var existing = await context.Auctions.Where(a => ids.Contains(a.Id)).Select(a => a.Id).ToListAsync();
        var missing = demos.Where(d => !existing.Contains(d.Id)).ToList();
        if (missing.Count == 0) return [];

        var images = services.GetRequiredService<ListingImageService>();
        var partTypes = await context.PartTypes.Include(p => p.Category).ThenInclude(c => c!.Parent).ToListAsync();
        var now = DateTime.UtcNow;
        var added = new List<string>();

        foreach (var demo in missing)
        {
            var partType = demo.PartType is null ? null : partTypes.FirstOrDefault(p => p.Name == demo.PartType);
            var auction = new Auction
            {
                Id = demo.Id,
                Seller = demo.Seller,
                // The demo accounts keep their usernames; the name buyers see is the new brand.
                SellerName = demo.Seller.Replace("BidMyParts_", "ShopCarParts "),
                Status = Status.Live,
                BidIncrement = 1,
                CreatedAt = now,
                UpdatedAt = now,
                AuctionEnd = demo.DurationHours > 0 ? now.AddHours(demo.DurationHours) : now.AddDays(demo.Days),
                SellerEmail = $"{demo.Seller.ToLowerInvariant()}@example.com",
                Item = new Item
                {
                    Id = demo.Id,
                    Kind = demo.Kind,
                    Make = "",
                    Model = "",
                    Description = "",
                    PartType = partType,
                    PartTypeId = partType?.Id,
                    PartDetails = demo.Kind == ListingKind.Vehicle ? null : new PartDetails { ItemId = demo.Id },
                    VehicleDetails = demo.Kind == ListingKind.Vehicle ? new VehicleDetails { ItemId = demo.Id } : null,
                }
            };

            var d = demo.Details;
            d.PartPrice = demo.Price;
            d.DurationDays = demo.DurationHours > 0 ? 1 : demo.Days;
            d.CountryCode = demo.Country;
            d.LocationName = demo.Place;
            d.Latitude = demo.Lat;
            d.Longitude = demo.Lon;
            // Vehicles are picked up; parts can also be shipped within the country (the dash cam ships free).
            d.DeliveryOptions = demo.Kind == ListingKind.Vehicle ? ["Pickup"] : ["Pickup", "Shipping"];
            if (demo.Kind != ListingKind.Vehicle)
                d.ShippingFee = demo.PartType == "Dash cam" ? 0 : DemoShippingFees.GetValueOrDefault(demo.Country, 0);
            d.SizeClass = demo.Kind == ListingKind.Vehicle ? null : demo.PartType is "Bumper" or "Bonnet" ? "Large" : demo.PartType == "Gearbox" ? "Freight" : "Small";
            if (demo.FixedPriceUnits > 0)
            {
                d.SaleType = nameof(SaleType.FixedPrice);
                d.PartDetails!.Quantity = demo.FixedPriceUnits;
            }
            ListingRules.Apply(d, auction, partType);
            auction.AuctionEnd = auction.SaleType == SaleType.FixedPrice ? ListingRules.NoEnd : auction.AuctionEnd;

            // Photos: 3 for parts, the 8 guided angles for vehicles.
            var captions = demo.Kind == ListingKind.Vehicle
                ? ListingRules.VehicleAngles.ToList()
                : ["Front", "Back", "Label / part number"];
            var colour = Colours.GetValueOrDefault(demo.Country, SKColors.DarkSlateBlue);
            var title = demo.Kind == ListingKind.Vehicle ? $"{auction.Item.Year} {auction.Item.Make} {auction.Item.Model}" : partType?.Name ?? "Part";
            for (var i = 0; i < captions.Count; i++)
            {
                var bytes = ImageProcessor.Placeholder(title, captions[i], colour);
                var image = await images.StoreAsync(bytes, ".jpg", demo.Id);
                if (image is null) continue;
                image.SortOrder = i;
                image.IsPrimary = i == 0;
                image.Angle = demo.Kind == ListingKind.Vehicle ? captions[i] : null;
                auction.Item.Images.Add(image);
            }

            var errors = ListingRules.ValidateForPublish(auction);
            if (errors.Count > 0)
            {
                Console.WriteLine($"Demo listing {demo.Id} skipped: {string.Join("; ", errors.SelectMany(e => e.Value))}");
                foreach (var image in auction.Item.Images) await images.DeleteFilesAsync(image);
                continue;
            }

            context.Auctions.Add(auction);
            added.Add(demo.Id);
        }

        await context.SaveChangesAsync();
        Console.WriteLine($"Added {added.Count} ShopCarParts demo listings");
        return added;
    }

    /// <summary>
    /// Converts the older demo items (no listing kind yet) to the listing model: vehicles get vehicle details, parts get
    /// a part type, a fitment row from their make/model/years, part numbers, details and the generated title.
    /// No reserve any more: reserve = part price. Returns the ids to re-publish.
    /// </summary>
    public static List<string> ConvertExisting(AuctionDbContext context)
    {
        var old = context.Auctions.WithDetails()
            .Where(a => a.Item.Kind == null && a.Status != Status.Draft)
            .ToList();
        if (old.Count == 0) return [];

        var partTypes = context.PartTypes.Include(p => p.Category).ThenInclude(c => c!.Parent).ToList();
        foreach (var auction in old)
        {
            var item = auction.Item;
            var country = item.Latitude is { } lat && item.Longitude is { } lon
                ? RegionRules.SupportedCountries.FirstOrDefault(c => CountryBounds.Contains(c, lat, lon))
                : null;
            item.CountryCode = country ?? "IN";
            item.Currency = RegionRules.CurrencyFor(item.CountryCode);
            item.DeliveryOptions = DeliveryOptions.Pickup;
            item.DurationDays ??= 7;
            auction.ReservePrice = auction.StartingPrice;

            if (item.ItemType == ItemType.Vehicle)
            {
                item.Kind = ListingKind.Vehicle;
                item.VehicleDetails ??= new VehicleDetails
                {
                    ItemId = item.Id, Fuel = "Petrol", Transmission = "Automatic", RunningStatus = "RunsAndDrives",
                    DamageType = "None", MileageUnit = RegionRules.DistanceUnit(item.CountryCode)
                };
            }
            else
            {
                var name = (item.PartName ?? "").ToLowerInvariant();
                var type = MatchPartType(name, partTypes);
                item.PartType = type;
                item.PartTypeId = type?.Id;
                item.Kind = type?.FitmentMode is FitmentMode.Universal or FitmentMode.SizeBased
                    ? ListingKind.CommonPart
                    : ListingKind.VehicleSpecificPart;
                item.PartDetails ??= new PartDetails
                {
                    ItemId = item.Id,
                    BrandType = BrandType.GenuineOem,
                    UsedGrade = item.Condition == ItemCondition.Used ? UsedGrade.B : null,
                    Position = type?.HasPosition == true ? (name.Contains("rear") ? "Rear" : "Front") : null,
                    Side = type?.HasSide == true ? (name.Contains("right") ? "Right" : name.Contains("left") ? "Left" : "Centre") : null
                };
                if (item.Fitments.Count == 0 && item.Kind == ListingKind.VehicleSpecificPart && item.Make.Length > 0)
                    item.Fitments.Add(new PartFitment
                    {
                        ItemId = item.Id, YearFrom = item.Year, YearTo = item.YearTo ?? item.Year, Make = item.Make, Model = item.Model
                    });
                if (item.PartNumbers.Count == 0 && PartNumbers.Normalize(item.PartNumber) is { } normalized)
                    item.PartNumbers.Add(new PartNumber { ItemId = item.Id, Value = item.PartNumber!, Normalized = normalized });
            }

            ListingRules.SyncDerivedFields(item);
            auction.UpdatedAt = DateTime.UtcNow;
        }

        context.SaveChanges();
        Console.WriteLine($"Converted {old.Count} demo items to the listing model");
        return old.Select(a => a.Id).ToList();
    }

    /// <summary>The part type whose name or synonym appears in the old part name (longest match wins).</summary>
    private static PartType? MatchPartType(string partName, List<PartType> types) =>
        types
            .SelectMany(t => new[] { t.Name.ToLowerInvariant() }.Concat(t.Synonyms).Select(word => (Type: t, Word: word)))
            .Where(x => x.Word.Length > 2 && partName.Contains(x.Word))
            .OrderByDescending(x => x.Word.Length)
            .Select(x => x.Type)
            .FirstOrDefault();
}
