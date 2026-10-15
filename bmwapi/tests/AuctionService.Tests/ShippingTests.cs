using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using AuctionService.Controllers;
using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using AuctionService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuctionService.Tests;

/// <summary>The shipping formula: base + distance × rate, rounded, min / max, max distance, miles in the USA.</summary>
public class ShippingCalculatorTests
{
    private static ShippingRate Rate(decimal baseFee, decimal rate, decimal min, decimal max, int maxDistance, string unit = "km", string country = "IN") => new()
    {
        CountryCode = country, SizeClass = SizeClass.Medium, Currency = "INR", Unit = unit,
        BaseFee = baseFee, RatePerUnit = rate, MinFee = min, MaxFee = max, MaxDistance = maxDistance, IsPlaceholder = true,
    };

    // Kukatpally → Secunderabad (Hyderabad): ~16.6 km straight line.
    private const double KukatpallyLat = 17.4849, KukatpallyLon = 78.4138, SecunderabadLat = 17.4399, SecunderabadLon = 78.4983;

    [Fact]
    public void Distance_IsStraightLineTimesTheRoadFactor()
    {
        var straight = Contracts.GeoMath.HaversineKm(KukatpallyLat, KukatpallyLon, SecunderabadLat, SecunderabadLon);
        var road = ShippingCalculator.Distance(KukatpallyLat, KukatpallyLon, SecunderabadLat, SecunderabadLon, "km");
        Assert.Equal(straight * 1.3, road, 6);
    }

    [Theory]
    // base 120 + 10 km × 2 = 140
    [InlineData(10, 140)]
    // 120 + 12.4 × 2 = 144.8 → 145 (whole number, half away from zero)
    [InlineData(12.4, 145)]
    // 120 + 0 = 120
    [InlineData(0, 120)]
    public void Charge_IsBasePlusDistanceTimesRate_Rounded(double distance, int expected) =>
        Assert.Equal(expected, ShippingCalculator.Charge(Rate(120, 2, 120, 1500, 2500), distance));

    [Fact]
    public void Charge_IsKeptBetweenTheMinimumAndMaximum()
    {
        Assert.Equal(200, ShippingCalculator.Charge(Rate(50, 1, 200, 1000, 5000), 10));   // 60 → min 200
        Assert.Equal(1000, ShippingCalculator.Charge(Rate(50, 1, 200, 1000, 5000), 4000)); // 4050 → max 1000
    }

    [Fact]
    public void Quote_WithinTheCountry_UsesTheRoadDistance()
    {
        var quote = ShippingCalculator.Quote(Rate(120, 2, 120, 1500, 2500), "IN", KukatpallyLat, KukatpallyLon, "IN",
            SecunderabadLat, SecunderabadLon, SizeClass.Medium);

        Assert.True(quote.Available);
        var road = ShippingCalculator.Distance(KukatpallyLat, KukatpallyLon, SecunderabadLat, SecunderabadLon, "km");
        Assert.Equal((int)Math.Round(120 + road * 2, MidpointRounding.AwayFromZero), quote.Charge);
        Assert.Equal("km", quote.Unit);
        Assert.True(quote.PlaceholderRates);
    }

    [Fact]
    public void Quote_AboveTheMaxDistance_IsNotAvailable_AndSaysWhy()
    {
        // Hyderabad → Delhi: ~1,250 km straight, ~1,630 km by road; this size class goes up to 1,000 km.
        var quote = ShippingCalculator.Quote(Rate(800, 12, 800, 15000, 1000), "IN", 17.385, 78.4867, "IN", 28.6139, 77.2090, SizeClass.Freight);

        Assert.False(quote.Available);
        Assert.Null(quote.Charge);
        Assert.Contains("Too far to ship", quote.Reason);
        Assert.Contains("up to 1,000 km", quote.Reason);
    }

    [Fact]
    public void Quote_ToAnotherCountry_IsNotAvailable()
    {
        var quote = ShippingCalculator.Quote(Rate(120, 2, 120, 1500, 2500), "IN", 17.385, 78.4867, "NG", 6.5244, 3.3792, SizeClass.Medium);

        Assert.False(quote.Available);
        Assert.Equal("Ships within India only", quote.Reason);
    }

    [Fact]
    public void Usa_UsesMiles()
    {
        // New York → Philadelphia: ~80 mi straight → ~104 mi by road. $15 + 104 × 0.30 ≈ $46.
        var rate = Rate(15, 0.30m, 15, 150, 3000, "mi", "US");
        var miles = ShippingCalculator.Distance(40.7128, -74.0060, 39.9526, -75.1652, "mi");
        var km = ShippingCalculator.Distance(40.7128, -74.0060, 39.9526, -75.1652, "km");
        Assert.Equal(km / Contracts.RegionRules.KmPerMile, miles, 6);

        var quote = ShippingCalculator.Quote(rate, "US", 40.7128, -74.0060, "US", 39.9526, -75.1652, SizeClass.Medium);
        Assert.Equal("mi", quote.Unit);
        Assert.Equal((int)Math.Round(miles, MidpointRounding.AwayFromZero), quote.Distance);
        Assert.Equal((int)Math.Round(15 + (decimal)miles * 0.30m, MidpointRounding.AwayFromZero), quote.Charge);
    }

    [Theory]
    [InlineData("Bulb", SizeClass.Small)]
    [InlineData("Headlamp", SizeClass.Medium)]
    [InlineData("Bumper", SizeClass.Large)]
    [InlineData("Door", SizeClass.Large)]
    [InlineData("Engine assembly", SizeClass.Freight)]
    [InlineData("Gearbox", SizeClass.Freight)]
    [InlineData("Something new", SizeClass.Medium)]
    public void PartTypes_HaveADefaultSizeClass(string partType, SizeClass expected) =>
        Assert.Equal(expected, ShippingRules.DefaultSizeClass(ListingKind.CommonPart, partType));

    [Fact]
    public void Vehicles_AreAlwaysVehicle() =>
        Assert.Equal(SizeClass.Vehicle, ShippingRules.DefaultSizeClass(ListingKind.Vehicle, "Bumper"));
}

/// <summary>Shipping through the API: estimates, the charge fixed on the order, pickup = 0, admin rates.</summary>
[Collection("auction-service")]
public class ShippingApiTests(AppFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        db.Orders.RemoveRange(db.Orders);
        db.Auctions.RemoveRange(db.Auctions);
        // Back to the seeded placeholder values.
        db.ShippingRates.RemoveRange(db.ShippingRates);
        await db.SaveChangesAsync();
        ShippingRateSeed.Seed(db);
    }

    // The item is in Kukatpally, Hyderabad.
    private const double ItemLat = 17.4849, ItemLon = 78.4138;
    // Secunderabad: ~16.6 km straight → ~21.6 km by road.
    private const double BuyerLat = 17.4399, BuyerLon = 78.4983;

    private async Task<Alba.IScenarioResult> Call(Action<Alba.Scenario> configure, string user = "bob", string role = "seller", HttpStatusCode status = HttpStatusCode.OK)
    {
        return await fixture.Host.Scenario(s =>
        {
            s.RemoveClaim("role");
            s.WithClaim(new Claim("role", role));
            s.WithClaim(new Claim("username", user));
            configure(s);
            s.StatusCodeShouldBe(status);
        });
    }

    private async Task<T> Json<T>(Action<Alba.Scenario> configure, string user = "bob", string role = "seller", HttpStatusCode status = HttpStatusCode.OK) =>
        await (await Call(configure, user, role, status)).ReadAsJsonAsync<T>();

    private async Task<AuctionDto> LiveListing(string partType, string[] delivery, string? sizeClass = null, int quantity = 2)
    {
        int partTypeId;
        using (var scope = fixture.Host.Services.CreateScope())
            partTypeId = (await scope.ServiceProvider.GetRequiredService<AuctionDbContext>().PartTypes.FirstAsync(p => p.Name == partType)).Id;

        var draft = await Json<AuctionDto>(s => s.Post.Json(new CreateListingDto
        {
            Kind = "CommonPart", PartTypeId = partTypeId, CountryCode = "IN", LocationName = "Kukatpally, Hyderabad", Latitude = ItemLat, Longitude = ItemLon,
        }).ToUrl("/api/listings"), status: HttpStatusCode.Created);

        var saved = await Json<AuctionDto>(s => s.Put.Json(new SaveListingDto
        {
            SaleType = "FixedPrice",
            PartDetails = new PartDetailsDto { BrandType = "Aftermarket", Brand = "Bosch", Condition = "New", Quantity = quantity },
            PartPrice = 4500,
            DeliveryOptions = [.. delivery],
            SizeClass = sizeClass,
            // An older app's manual fee is ignored.
            ShippingFee = 999,
        }).ToUrl($"/api/listings/{draft.Id}"));
        Assert.Null(saved.ShippingFee);

        var form = new MultipartFormDataContent();
        for (var i = 0; i < 3; i++)
        {
            var content = new ByteArrayContent(TestImages.Jpeg());
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(content, "files", $"{i}.jpg");
        }
        await Call(s => s.Post.MultipartFormData(form).ToUrl($"/api/auctions/{draft.Id}/images"));
        return await Json<AuctionDto>(s => s.Post.Json(new { }).ToUrl($"/api/listings/{draft.Id}/publish"));
    }

    private Task<OrderDto> Buy(string id) =>
        Json<OrderDto>(s => s.Post.Json(new BuyListingDto { Quantity = 1 }).ToUrl($"/api/listings/{id}/buy"), "carol", "buyer");

    private static AddressDto Address(string country = "IN") => new()
    {
        Name = "Carol", Phone = "+919876543210", Line1 = "12 MG Road", City = "Secunderabad", Postcode = "500003", CountryCode = country,
    };

    private static int ExpectedCharge(decimal baseFee, decimal rate, double lat = BuyerLat, double lon = BuyerLon) =>
        (int)Math.Round(baseFee + (decimal)ShippingCalculator.Distance(ItemLat, ItemLon, lat, lon, "km") * rate, MidpointRounding.AwayFromZero);

    [Fact]
    public async Task Listing_GetsTheDefaultSizeClass_TheSellerCanChangeIt()
    {
        var headlamp = await LiveListing("Dash cam", ["Pickup", "Shipping"]);
        Assert.Equal("Small", headlamp.SizeClass);

        var bigLamp = await LiveListing("Dash cam", ["Shipping"], sizeClass: "Large");
        Assert.Equal("Large", bigLamp.SizeClass);
    }

    [Fact]
    public async Task Estimate_ForTheBuyersLocation_PickupFree()
    {
        var listing = await LiveListing("Dash cam", ["Pickup", "Shipping"]);

        var estimate = await Json<ShippingEstimateDto>(s => s.Get.Url(
            $"/api/shipping/estimate?listingId={listing.Id}&lat={BuyerLat}&lon={BuyerLon}&country=IN"), "carol", "buyer");

        Assert.True(estimate.PickupOffered);
        Assert.True(estimate.Shipping!.Available);
        // Placeholder India Small: ₹60 + ₹1 / km.
        Assert.Equal(ExpectedCharge(60, 1), estimate.Shipping.Charge);
        Assert.Equal("INR", estimate.Shipping.Currency);
        Assert.True(estimate.Shipping.PlaceholderRates);

        var abroad = await Json<ShippingEstimateDto>(s => s.Get.Url(
            $"/api/shipping/estimate?listingId={listing.Id}&lat=6.5244&lon=3.3792&country=NG"), "carol", "buyer");
        Assert.False(abroad.Shipping!.Available);
        Assert.Equal("Ships within India only", abroad.Shipping.Reason);
    }

    [Fact]
    public async Task ChoosingShipping_CalculatesAndFixesTheCharge_TotalIncludesIt_LaterRateChangesDontChangeIt()
    {
        var listing = await LiveListing("Dash cam", ["Pickup", "Shipping"]);
        var order = await Buy(listing.Id);
        Assert.Null(order.ShippingCharge);

        // Without a delivery location there's no charge to work out.
        var noLocation = await (await Call(s => s.Post.Json(new ChooseDeliveryDto { Method = "Shipping", Address = Address() })
            .ToUrl($"/api/orders/{order.Id}/delivery-method"), "carol", "buyer", HttpStatusCode.BadRequest)).ReadAsTextAsync();
        Assert.Contains("LOCATION_REQUIRED", noLocation);

        var shipped = await Json<OrderDto>(s => s.Post.Json(new ChooseDeliveryDto
        {
            Method = "Shipping", Address = Address(), Latitude = BuyerLat, Longitude = BuyerLon,
        }).ToUrl($"/api/orders/{order.Id}/delivery-method"), "carol", "buyer");

        var charge = ExpectedCharge(60, 1);
        Assert.Equal(charge, shipped.ShippingCharge);
        Assert.Equal(4500 + charge, shipped.Total);
        Assert.Equal("km", shipped.ShippingDistanceUnit);
        Assert.Equal("Small", shipped.ShippingSizeClass);
        Assert.Equal(BuyerLat, shipped.ShipTo!.Latitude);

        // The admin raises the rate: the order keeps its charge; a new estimate uses the new rate.
        await Call(s => s.Put.Json(new UpdateShippingRateDto { BaseFee = 500, RatePerUnit = 10, MinFee = 500, MaxFee = 5000, MaxDistance = 2500 })
            .ToUrl("/api/shipping/rates/IN/Small"), "admin", "admin");

        var seller = await Json<OrderDto>(s => s.Get.Url($"/api/orders/{order.Id}"));
        Assert.Equal(charge, seller.ShippingCharge);
        Assert.Equal(4500 + charge, seller.Total);

        var estimate = await Json<ShippingEstimateDto>(s => s.Get.Url(
            $"/api/shipping/estimate?listingId={listing.Id}&lat={BuyerLat}&lon={BuyerLon}&country=IN"), "carol", "buyer");
        Assert.Equal(ExpectedCharge(500, 10), estimate.Shipping!.Charge);
        Assert.False(estimate.Shipping.PlaceholderRates);
    }

    [Fact]
    public async Task ChoosingPickup_ChargeIsZero_TotalIsTheItemPrice()
    {
        var listing = await LiveListing("Dash cam", ["Pickup", "Shipping"]);
        var order = await Buy(listing.Id);

        var pickup = await Json<OrderDto>(s => s.Post.Json(new ChooseDeliveryDto { Method = "Pickup" })
            .ToUrl($"/api/orders/{order.Id}/delivery-method"), "carol", "buyer");

        Assert.Equal(0, pickup.ShippingCharge);
        Assert.Equal(4500, pickup.Total);
    }

    [Fact]
    public async Task Shipping_TooFar_IsRefusedWithTheReason()
    {
        var listing = await LiveListing("Dash cam", ["Shipping"], sizeClass: "Freight");
        var order = await Buy(listing.Id);

        // Delhi: ~1,630 km by road; placeholder India Freight goes up to 1,500 km.
        var body = await (await Call(s => s.Post.Json(new ChooseDeliveryDto
        {
            Method = "Shipping", Address = new AddressDto { Name = "Carol", Phone = "+919876543210", Line1 = "1 CP", City = "Delhi", Postcode = "110001", CountryCode = "IN" },
            Latitude = 28.6139, Longitude = 77.2090,
        }).ToUrl($"/api/orders/{order.Id}/delivery-method"), "carol", "buyer", HttpStatusCode.BadRequest)).ReadAsTextAsync();

        Assert.Contains("SHIPPING_NOT_AVAILABLE", body);
        Assert.Contains("Too far to ship", body);
    }

    [Fact]
    public async Task Rates_AreSeededAsPlaceholders_OnlyAnAdminChangesThem()
    {
        var india = await Json<List<ShippingRateDto>>(s => s.Get.Url("/api/shipping/rates?country=IN"), "carol", "buyer");
        Assert.Equal(["Small", "Medium", "Large", "Freight", "Vehicle"], india.Select(r => r.SizeClass));
        Assert.All(india, r => Assert.True(r.IsPlaceholder));
        Assert.Equal((60m, 1m), (india[0].BaseFee, india[0].RatePerUnit));
        Assert.Equal((2000m, 25m), (india[4].BaseFee, india[4].RatePerUnit));

        var usa = await Json<List<ShippingRateDto>>(s => s.Get.Url("/api/shipping/rates?country=US"), "carol", "buyer");
        Assert.All(usa, r => Assert.Equal(("mi", "USD"), (r.Unit, r.Currency)));

        var denied = await (await Call(s => s.Put.Json(new UpdateShippingRateDto { BaseFee = 1, RatePerUnit = 1, MinFee = 1, MaxFee = 2, MaxDistance = 10 })
            .ToUrl("/api/shipping/rates/IN/Small"), "bob", "seller", HttpStatusCode.Forbidden)).ReadAsTextAsync();
        Assert.Contains("ADMIN_ONLY", denied);
    }
}
