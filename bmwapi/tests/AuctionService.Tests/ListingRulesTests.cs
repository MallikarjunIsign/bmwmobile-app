using AuctionService.DTOs;
using AuctionService.Entities;
using AuctionService.Services;

namespace AuctionService.Tests;

public class ListingRulesTests
{
    private static readonly PartType Bumper = new()
    {
        Id = 1, Name = "Bumper", FitmentMode = FitmentMode.VehicleSpecific, HasPosition = true, IsBodyPart = true,
        Category = new Category { Name = "Body – Exterior", LegacyCategory = PartCategory.Body }
    };

    private static readonly PartType Tyre = new()
    {
        Id = 2, Name = "Tyre", FitmentMode = FitmentMode.SizeBased,
        Category = new Category { Name = "Wheels & Tyres", LegacyCategory = PartCategory.WheelsAndTyres },
        SpecFields =
        [
            new SpecFieldDefinition { Key = "width", Label = "Width", Type = "number" },
            new SpecFieldDefinition { Key = "profile", Label = "Profile", Type = "number" },
            new SpecFieldDefinition { Key = "rim", Label = "Rim size", Type = "number" },
        ]
    };

    private static readonly PartType DashCam = new()
    {
        Id = 3, Name = "Dash cam", FitmentMode = FitmentMode.Universal,
        Category = new Category { Name = "Accessories & Tools", LegacyCategory = PartCategory.Other }
    };

    private static Auction Draft(ListingKind kind, PartType? type, Action<SaveListingDto>? configure = null, int photos = 3)
    {
        var id = Guid.NewGuid().ToString();
        var auction = new Auction
        {
            Id = id, Seller = "bob", Status = Status.Draft,
            Item = new Item
            {
                Id = id, Kind = kind, Make = "", Model = "", Description = "", PartType = type, PartTypeId = type?.Id,
                PartDetails = kind == ListingKind.Vehicle ? null : new PartDetails { ItemId = id },
                VehicleDetails = kind == ListingKind.Vehicle ? new VehicleDetails { ItemId = id } : null
            }
        };
        var dto = new SaveListingDto
        {
            PartPrice = 300, DurationDays = 7, CountryCode = "US", Latitude = 29.76, Longitude = -95.37,
            LocationName = "Houston", DeliveryOptions = ["Pickup"]
        };
        configure?.Invoke(dto);
        ListingRules.Apply(dto, auction, type);
        for (var i = 0; i < photos; i++)
            auction.Item.Images.Add(new ItemImage { ItemId = id, Url = $"/images/{i}.jpg", FileName = $"{i}.jpg", SortOrder = i });
        return auction;
    }

    private static void HiluxBumper(SaveListingDto d)
    {
        d.Fitments = [new FitmentDto { YearFrom = 2016, YearTo = 2020, Make = "Toyota", Model = "Hilux" }];
        d.PartDetails = new PartDetailsDto { BrandType = "GenuineOem", Condition = "Used", UsedGrade = "A", Position = "Front" };
        d.PartNumbers = [new PartNumberDto { Value = "52119-0K935" }, new PartNumberDto { Value = "52119 0k935" }];
    }

    [Fact]
    public void Title_VehicleSpecificPart_IsGenerated()
    {
        var auction = Draft(ListingKind.VehicleSpecificPart, Bumper, HiluxBumper);

        Assert.Equal("2016–2020 Toyota Hilux Front Bumper – Genuine OEM – Used (Grade A)", auction.Item.Title);
        Assert.Equal("Fits Toyota Hilux 2016–2020", ListingRules.FitmentSummary(auction.Item));
    }

    [Fact]
    public void Title_SellerWordsAreAppended_GeneratedPartStays()
    {
        var auction = Draft(ListingKind.VehicleSpecificPart, Bumper, d => { HiluxBumper(d); d.TitleExtra = "with fog lamp holes"; });

        Assert.StartsWith("2016–2020 Toyota Hilux Front Bumper – Genuine OEM – Used (Grade A)", auction.Item.Title);
        Assert.EndsWith("– with fog lamp holes", auction.Item.Title);
    }

    [Fact]
    public void Title_SizeBasedAndUniversal()
    {
        var tyre = Draft(ListingKind.CommonPart, Tyre, d =>
        {
            d.Specs = new() { ["width"] = "265", ["profile"] = "65", ["rim"] = "17" };
            d.PartDetails = new PartDetailsDto { BrandType = "Aftermarket", Brand = "Bridgestone", Condition = "New" };
        });
        var cam = Draft(ListingKind.CommonPart, DashCam, d =>
            d.PartDetails = new PartDetailsDto { BrandType = "Aftermarket", Brand = "70mai", Condition = "New" });

        Assert.Equal("Tyre 265/65 R17 – Bridgestone – New", tyre.Item.Title);
        Assert.Equal("Dash Cam – 70mai – New", cam.Item.Title);
        Assert.Equal(FitmentMode.SizeBased, tyre.Item.FitmentMode);
        Assert.Equal(FitmentMode.Universal, cam.Item.FitmentMode);
    }

    [Fact]
    public void PartNumbers_StoredAsTypedAndNormalised_WithoutDuplicates()
    {
        var auction = Draft(ListingKind.VehicleSpecificPart, Bumper, HiluxBumper);

        var number = Assert.Single(auction.Item.PartNumbers);
        Assert.Equal("52119-0K935", number.Value);
        Assert.Equal("521190K935", number.Normalized);
        Assert.Equal("52119-0K935", auction.Item.PartNumber);
    }

    [Fact]
    public void CompleteListings_PassValidation()
    {
        Assert.Empty(ListingRules.ValidateForPublish(Draft(ListingKind.VehicleSpecificPart, Bumper, HiluxBumper)));
        Assert.Empty(ListingRules.ValidateForPublish(Draft(ListingKind.CommonPart, DashCam, d =>
            d.PartDetails = new PartDetailsDto { BrandType = "Aftermarket", Brand = "70mai", Condition = "New" })));
    }

    [Fact]
    public void VehicleSpecific_NeedsAFitmentRow() =>
        Assert.Contains("fitments", ListingRules.ValidateForPublish(Draft(ListingKind.VehicleSpecificPart, Bumper, d =>
        {
            HiluxBumper(d);
            d.Fitments = [];
        })).Keys);

    [Theory]
    [InlineData(1980, 2020)] // 41 years
    [InlineData(2020, 2016)] // from after to
    public void Fitment_YearRangeRules(int from, int to) =>
        Assert.Contains("fitments", ListingRules.ValidateForPublish(Draft(ListingKind.VehicleSpecificPart, Bumper, d =>
        {
            HiluxBumper(d);
            d.Fitments = [new FitmentDto { YearFrom = from, YearTo = to, Make = "Toyota", Model = "Hilux" }];
        })).Keys);

    [Fact]
    public void Fitment_AtMost50RowsAndNoDuplicates()
    {
        var many = Draft(ListingKind.VehicleSpecificPart, Bumper, d =>
        {
            HiluxBumper(d);
            d.Fitments = Enumerable.Range(0, 51).Select(i => new FitmentDto { YearFrom = 2000, YearTo = 2001, Make = "Toyota", Model = $"M{i}" }).ToList();
        });
        var duplicate = Draft(ListingKind.VehicleSpecificPart, Bumper, d =>
        {
            HiluxBumper(d);
            d.Fitments = [new FitmentDto { YearFrom = 2016, YearTo = 2020, Make = "Toyota", Model = "Hilux" },
                          new FitmentDto { YearFrom = 2016, YearTo = 2020, Make = "toyota", Model = "HILUX" }];
        });

        Assert.Contains("fitments", ListingRules.ValidateForPublish(many).Keys);
        Assert.Contains(ListingRules.ValidateForPublish(duplicate)["fitments"], m => m.Contains("twice"));
    }

    [Fact]
    public void SizeBased_NeedsItsSpecFields()
    {
        var errors = ListingRules.ValidateForPublish(Draft(ListingKind.CommonPart, Tyre, d =>
        {
            d.Specs = new() { ["width"] = "265" };
            d.PartDetails = new PartDetailsDto { BrandType = "GenuineOem", Condition = "New" };
        }));

        Assert.Contains(errors["specs"], m => m.Contains("Profile"));
        Assert.Contains(errors["specs"], m => m.Contains("Rim size"));
    }

    [Fact]
    public void Universal_DropsVehicleDetails()
    {
        var cam = Draft(ListingKind.CommonPart, DashCam, d =>
        {
            d.Fitments = [new FitmentDto { YearFrom = 2016, YearTo = 2020, Make = "Toyota", Model = "Hilux" }];
            d.Specs = new() { ["size"] = "x" };
            d.PartDetails = new PartDetailsDto { BrandType = "Aftermarket", Brand = "70mai", Condition = "New" };
        });

        Assert.Empty(cam.Item.Fitments);
        Assert.Empty(cam.Item.Specs);
        Assert.Equal("", cam.Item.Make);
    }

    [Fact]
    public void CommonPart_WithAVehicleSpecificPartType_IsRejected() =>
        Assert.Contains("partTypeId", ListingRules.ValidateForPublish(Draft(ListingKind.CommonPart, Bumper, HiluxBumper)).Keys);

    [Fact]
    public void Details_RulesFollowThePartType()
    {
        var errors = ListingRules.ValidateForPublish(Draft(ListingKind.VehicleSpecificPart, Bumper, d =>
        {
            HiluxBumper(d);
            d.PartDetails = new PartDetailsDto { BrandType = "Aftermarket", Condition = "Used" };
        }));

        Assert.Contains("brand", errors.Keys);      // not OEM → brand needed
        Assert.Contains("usedGrade", errors.Keys);  // used → grade needed
        Assert.Contains("position", errors.Keys);   // bumper has a position
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(11, false)]
    public void Photos_PartsNeed3To10(int photos, bool ok) =>
        Assert.Equal(ok, !ListingRules.ValidateForPublish(Draft(ListingKind.VehicleSpecificPart, Bumper, HiluxBumper, photos)).ContainsKey("photos"));

    [Fact]
    public void Vehicle_NeedsIdentityConditionPapersAnd8Photos()
    {
        var incomplete = Draft(ListingKind.Vehicle, null, d => { d.Year = 2017; d.Make = "Honda"; d.Model = "Civic"; }, photos: 3);
        var errors = ListingRules.ValidateForPublish(incomplete);
        Assert.Contains("photos", errors.Keys);
        Assert.Contains("fuel", errors.Keys);
        Assert.Contains("papers", errors.Keys); // USA: title status

        var complete = Draft(ListingKind.Vehicle, null, d =>
        {
            d.Year = 2017; d.Make = "Honda"; d.Model = "Civic"; d.Mileage = 61000;
            d.Vehicle = new VehicleDetailsDto
            {
                Fuel = "Petrol", Transmission = "CVT", RunningStatus = "RunsAndDrives", DamageType = "None",
                Vin = "19XFC2F72HE123456", Papers = new() { ["TitleStatus"] = "Clean" }
            };
        }, photos: 8);
        Assert.Empty(ListingRules.ValidateForPublish(complete));
        Assert.Equal("2017 Honda Civic – Petrol CVT – 61,000 mi", complete.Item.Title);
        Assert.Equal("••••••123456", ListingRules.MaskVin(complete.Item.VehicleDetails!.Vin));
    }

    [Fact]
    public void AuctionSettings_DurationAndPrice()
    {
        var errors = ListingRules.ValidateForPublish(Draft(ListingKind.VehicleSpecificPart, Bumper, d =>
        {
            HiluxBumper(d);
            d.DurationDays = 4;
            d.PartPrice = 0;
        }));

        Assert.Contains("durationDays", errors.Keys);
        Assert.Contains("partPrice", errors.Keys);
    }

    [Fact]
    public void PartPrice_IsAlsoTheReserve_NoReservePrice()
    {
        var auction = Draft(ListingKind.VehicleSpecificPart, Bumper, d => { HiluxBumper(d); d.PartPrice = 450; });

        Assert.Equal(450, auction.StartingPrice);
        Assert.Equal(450, auction.ReservePrice);
    }

    [Theory]
    [InlineData("call me on +234 803 123 4567", true)]
    [InlineData("mail me: bob@example.com", true)]
    [InlineData("fits 2016-2020 models, part 52119", false)]
    public void ContactDetails_AreDetected(string text, bool expected) =>
        Assert.Equal(expected, ListingRules.ContainsContactDetails(text));

    [Fact]
    public void Shipping_NeedsAFeeOrFreeShipping_AndASizeClass()
    {
        var missing = ListingRules.ValidateForPublish(Draft(ListingKind.VehicleSpecificPart, Bumper, d =>
        {
            HiluxBumper(d);
            d.DeliveryOptions = ["Shipping"];
        }));
        Assert.Contains("sizeClass", missing.Keys);
        Assert.Contains("shippingFee", missing.Keys);

        // Free shipping is a fee of 0.
        var free = Draft(ListingKind.VehicleSpecificPart, Bumper, d =>
        {
            HiluxBumper(d);
            d.DeliveryOptions = ["Pickup", "Shipping"];
            d.ShippingFee = 0;
            d.SizeClass = "Large";
        });
        Assert.Empty(ListingRules.ValidateForPublish(free));
        Assert.Equal(DeliveryOptions.Pickup | DeliveryOptions.Shipping, free.Item.DeliveryOptions);
    }

    [Fact]
    public void Delivery_AtLeastOneOption_AndOnlyPickupOrShipping()
    {
        var none = ListingRules.ValidateForPublish(Draft(ListingKind.VehicleSpecificPart, Bumper, d =>
        {
            HiluxBumper(d);
            d.DeliveryOptions = [];
        }));
        Assert.Equal(["Choose Pickup, Shipping or both"], none["deliveryOptions"]);

        // "Buyer arranges" no longer exists; older apps' "SellerShips" means Shipping.
        var old = Draft(ListingKind.VehicleSpecificPart, Bumper, d => d.DeliveryOptions = ["BuyerArranges", "SellerShips"]);
        Assert.Equal(DeliveryOptions.Shipping, old.Item.DeliveryOptions);
    }

    [Fact]
    public void Delivery_UntickingShippingDropsItsFeeAndSize()
    {
        var auction = Draft(ListingKind.VehicleSpecificPart, Bumper, d =>
        {
            d.DeliveryOptions = ["Pickup", "Shipping"];
            d.ShippingFee = 500;
            d.SizeClass = "Large";
            d.PickupAddress = "Plot 12, Road 4";
        });
        ListingRules.Apply(new SaveListingDto { DeliveryOptions = ["Pickup"] }, auction, Bumper);

        Assert.Null(auction.Item.ShippingFee);
        Assert.Null(auction.Item.SizeClass);
        Assert.Equal("Plot 12, Road 4", auction.Item.PickupAddress);
    }

    [Theory]
    [InlineData("Kukatpally, Hyderabad", "Hyderabad")]
    [InlineData("Houston, TX", "Houston")]
    [InlineData("Dublin", "Dublin")]
    [InlineData("Swords, Dublin", "Dublin")]
    [InlineData(null, null)]
    public void PickupCity_IsTheCityOfTheItemLocation(string? location, string? city) =>
        Assert.Equal(city, ListingRules.PublicCity(location));

    private static void NewDashCam(SaveListingDto d) =>
        d.PartDetails = new PartDetailsDto { BrandType = "Aftermarket", Brand = "70mai", Condition = "New", Quantity = 4 };

    [Fact]
    public void AuctionEnd_ChosenBySeller_ReplacesTheDurationAndMustBe1HourTo30DaysAhead()
    {
        var now = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);
        Auction EndingAt(DateTime end) => Draft(ListingKind.CommonPart, DashCam, d => { NewDashCam(d); d.AuctionEnd = end; });

        var ok = EndingAt(now.AddDays(2).AddHours(3));
        Assert.Null(ok.Item.DurationDays);
        Assert.Empty(ListingRules.ValidateForPublish(ok, now));
        Assert.Equal(now.AddDays(2).AddHours(3), ListingRules.EndWhenPublished(ok, now));

        Assert.Contains("auctionEnd", ListingRules.ValidateForPublish(EndingAt(now.AddMinutes(30)), now).Keys);
        Assert.Contains("auctionEnd", ListingRules.ValidateForPublish(EndingAt(now.AddDays(31)), now).Keys);
    }

    [Fact]
    public void OlderApps_DurationDays_StillCountFromPublishing()
    {
        var now = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);
        var auction = Draft(ListingKind.CommonPart, DashCam, NewDashCam);

        Assert.Equal(7, auction.Item.DurationDays);
        Assert.Equal(now.AddDays(7), ListingRules.EndWhenPublished(auction, now));
    }

    [Fact]
    public void FixedPrice_HasNoEnd_AndSellsTheQuantity()
    {
        var now = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);
        var auction = Draft(ListingKind.CommonPart, DashCam, d => { NewDashCam(d); d.SaleType = "FixedPrice"; d.DurationDays = null; });

        Assert.Equal(SaleType.FixedPrice, auction.SaleType);
        Assert.Empty(ListingRules.ValidateForPublish(auction, now));
        Assert.Equal(ListingRules.NoEnd, ListingRules.EndWhenPublished(auction, now));
        Assert.Equal(4, auction.QuantityAvailable);

        // Editing a Live listing: the quantity can't drop to what's already sold.
        auction.Status = Status.Live;
        auction.QuantitySold = 3;
        Assert.Equal(1, auction.QuantityAvailable);
        auction.Item.Quantity = 3;
        Assert.Contains("quantity", ListingRules.ValidateForPublish(auction, now).Keys);
    }

    [Fact]
    public void FixedPrice_Vehicle_IsOneUnit()
    {
        var auction = Draft(ListingKind.Vehicle, null, d => d.SaleType = "FixedPrice", photos: 8);
        Assert.Equal(1, auction.QuantityAvailable);
    }
}
