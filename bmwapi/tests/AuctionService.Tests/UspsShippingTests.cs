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

/// <summary>
/// US shipping = USPS Ground Advantage retail from the official price list (USPS Notice 123, effective 2026-10-04).
/// Expected prices below are copied from that list.
/// </summary>
public class UspsGroundAdvantageTests
{
    private static readonly UspsGroundAdvantage Usps = UspsGroundAdvantage.FromFile();

    private static Parcel Box(decimal lb, decimal l = 10, decimal w = 8, decimal h = 6) => new(lb, l, w, h);

    [Fact]
    public void PriceList_IsTheOfficialEdition() => Assert.Equal("2026-10-04", Usps.Edition);

    [Theory]
    [InlineData(40, 1)]
    [InlineData(100, 2)]
    [InlineData(250, 3)]
    [InlineData(500, 4)]
    [InlineData(900, 5)]
    [InlineData(1200, 6)]
    [InlineData(1600, 7)]
    [InlineData(2500, 8)]
    public void Zones_FromTheDistance(double miles, int zone) => Assert.Equal(zone, UspsGroundAdvantage.Zone(miles));

    [Theory]
    // 4 oz, zone 1 → $8.40
    [InlineData(0.25, 40, 8.40)]
    // 8 oz, zone 8 → $10.20
    [InlineData(0.5, 2500, 10.20)]
    // 1 lb, zone 4 → $11.10
    [InlineData(1, 500, 11.10)]
    // 1.2 lb is charged as 2 lb: zone 8 → $19.80
    [InlineData(1.2, 2500, 19.80)]
    // 15 lb, zone 2 → $21.00
    [InlineData(15, 100, 21.00)]
    // 70 lb, zone 1 → $68.00
    [InlineData(70, 40, 68.00)]
    public void Price_ByWeightAndZone(double lb, double miles, double expected) =>
        Assert.Equal((decimal)expected, Usps.Price(Box((decimal)lb), miles)!.Price);

    [Fact]
    public void OverOneCubicFoot_ChargedOnTheDimensionalWeight()
    {
        // 20 × 14 × 12 = 3,360 in³ → 3,360 / 166 = 20.2 → 21 lb (actual 10 lb). Zone 1, 21 lb → $27.50.
        // 3,360 in³ is under 2 ft³ (3,456 in³), so there's no extra fee.
        var price = Usps.Price(Box(10, 20, 14, 12), 40)!;
        Assert.Equal(21 * 16, price.BillableWeightOz);
        Assert.Equal(27.50m, price.Price);
        Assert.Equal(0m, price.Fees);
    }

    [Fact]
    public void LongAndBigParcels_PayTheNonStandardFees()
    {
        // Longest side 25 in (22–30): +$4.50. 25 × 6 × 4 = 600 in³, 2 lb, zone 2 → $12.00 + $4.50.
        Assert.Equal(16.50m, Usps.Price(Box(2, 25, 6, 4), 100)!.Price);
        // Longest side 34 in (over 30): +$10.00. 34 × 6 × 4 = 816 in³, 2 lb, zone 2 → $12.00 + $10.00.
        Assert.Equal(22.00m, Usps.Price(Box(2, 34, 6, 4), 100)!.Price);
    }

    [Fact]
    public void LengthPlusGirthOver108_IsTheOversizedPrice()
    {
        // 50 + 2 × (20 + 10) = 110 in → oversized, zone 3 → $146.70, + $10.00 (longest over 30 in) + $21.00 (10,000 in³ > 2 ft³).
        var price = Usps.Price(Box(20, 50, 20, 10), 250)!;
        Assert.True(price.Oversized);
        Assert.Equal(146.70m + 10.00m + 21.00m, price.Price);
    }

    [Fact]
    public void Over70Lb_OrOver130InLengthPlusGirth_IsNotUsps()
    {
        Assert.Null(Usps.Price(Box(71), 100));
        // A bumper's default box: 72 + 2 × (20 + 14) = 140 in.
        Assert.Null(Usps.Price(ParcelDefaults.For("Bumper", SizeClass.Large), 100));
        Assert.Equal("over 130 in length + girth", UspsGroundAdvantage.TooBig(ParcelDefaults.For("Bumper", SizeClass.Large)));
    }

    [Fact]
    public void VehicleEstimate_NewYorkToLosAngeles_IsInThePublishedCoastToCoastRange()
    {
        // $350 + $0.70 × ~3,180 road miles ≈ $2,576 – inside the published $2,200–2,800 coast-to-coast range.
        var rate = new ShippingRate { CountryCode = "US", SizeClass = SizeClass.Vehicle, Currency = "USD", Unit = "mi",
            BaseFee = 350, RatePerUnit = 0.70m, MinFee = 400, MaxFee = 2800, MaxDistance = 3500, IsEstimate = true };
        var quote = ShippingCalculator.Quote(rate, "US", 40.7128, -74.0060, "US", 34.0522, -118.2437, SizeClass.Vehicle);

        Assert.True(quote.Available && quote.EstimatedRate);
        Assert.InRange(quote.Charge!.Value, 2200, 2800);
    }

    [Fact]
    public void PartTypes_HaveADefaultPackage()
    {
        Assert.Equal(new Parcel(8, 24, 16, 14), ParcelDefaults.For("Headlamp", SizeClass.Medium));
        Assert.Equal(new Parcel(0.5m, 6, 4, 4), ParcelDefaults.For("Bulb", SizeClass.Small));
        // Unknown part types: from the size class.
        Assert.Equal(new Parcel(2, 10, 8, 6), ParcelDefaults.For("Dash cam", SizeClass.Small));
    }
}

/// <summary>US shipping through the API: estimate, the fixed charge on the order, the seller's package, freight.</summary>
[Collection("auction-service")]
public class UsShippingApiTests(AppFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        db.Orders.RemoveRange(db.Orders);
        db.Auctions.RemoveRange(db.Auctions);
        await db.SaveChangesAsync();
    }

    // The item is in New York; the buyer in Philadelphia (~81 miles → USPS zone 2).
    private const double NycLat = 40.7128, NycLon = -74.0060, PhillyLat = 39.9526, PhillyLon = -75.1652;

    private async Task<T> Json<T>(Action<Alba.Scenario> configure, string user = "bob", string role = "seller", HttpStatusCode status = HttpStatusCode.OK)
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.RemoveClaim("role");
            s.WithClaim(new Claim("role", role));
            s.WithClaim(new Claim("username", user));
            configure(s);
            s.StatusCodeShouldBe(status);
        });
        return await result.ReadAsJsonAsync<T>();
    }

    private async Task<AuctionDto> UsListing(Action<SaveListingDto>? configure = null)
    {
        int partTypeId;
        using (var scope = fixture.Host.Services.CreateScope())
            partTypeId = (await scope.ServiceProvider.GetRequiredService<AuctionDbContext>().PartTypes.FirstAsync(p => p.Name == "Dash cam")).Id;

        var draft = await Json<AuctionDto>(s => s.Post.Json(new CreateListingDto
        {
            Kind = "CommonPart", PartTypeId = partTypeId, CountryCode = "US", LocationName = "Manhattan, New York", Latitude = NycLat, Longitude = NycLon,
        }).ToUrl("/api/listings"), status: HttpStatusCode.Created);

        var save = new SaveListingDto
        {
            SaleType = "FixedPrice",
            PartDetails = new PartDetailsDto { BrandType = "Aftermarket", Brand = "Garmin", Condition = "New", Quantity = 3 },
            PartPrice = 120,
            DeliveryOptions = ["Pickup", "Shipping"],
        };
        configure?.Invoke(save);
        await Json<AuctionDto>(s => s.Put.Json(save).ToUrl($"/api/listings/{draft.Id}"));

        var form = new MultipartFormDataContent();
        for (var i = 0; i < 3; i++)
        {
            var content = new ByteArrayContent(TestImages.Jpeg());
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(content, "files", $"{i}.jpg");
        }
        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "bob"));
            s.Post.MultipartFormData(form).ToUrl($"/api/auctions/{draft.Id}/images");
        });
        return await Json<AuctionDto>(s => s.Post.Json(new { }).ToUrl($"/api/listings/{draft.Id}/publish"));
    }

    private Task<ShippingEstimateDto> Estimate(string id, double lat = PhillyLat, double lon = PhillyLon, string country = "US") =>
        Json<ShippingEstimateDto>(s => s.Get.Url($"/api/shipping/estimate?listingId={id}&lat={lat}&lon={lon}&country={country}"), "carol", "buyer");

    [Fact]
    public async Task Estimate_IsTheUspsGroundAdvantagePrice_RoundedUpToAWholeDollar()
    {
        // Dash cam default package: 2 lb, 10 × 8 × 6 in. Zone 2, 2 lb → $12.00.
        var listing = await UsListing();
        Assert.Equal(2m, listing.WeightLb);

        var quote = (await Estimate(listing.Id)).Shipping!;
        Assert.True(quote.Available);
        Assert.Equal(12, quote.Charge);
        Assert.Equal(("USPS Ground Advantage", 2, "USD", "mi"), (quote.Service, quote.Zone, quote.Currency, quote.Unit));
        Assert.Equal(2m, quote.BillableWeightLb);
        Assert.Equal("2026-10-04", quote.PriceList);
        Assert.False(quote.PlaceholderRates);

        // Not to another country.
        var abroad = (await Estimate(listing.Id, 53.3498, -6.2603, "IE")).Shipping!;
        Assert.False(abroad.Available);
        Assert.Equal("Ships within USA only", abroad.Reason);
    }

    [Fact]
    public async Task TheSellersPackage_ChangesThePrice()
    {
        // 15 lb in a small box, zone 2 → $21.00.
        var listing = await UsListing(d => d.WeightLb = 15);
        Assert.Equal(21, (await Estimate(listing.Id)).Shipping!.Charge);

        // Coast to coast (Los Angeles, zone 8): 15 lb → $60.10 → $61.
        var la = (await Estimate(listing.Id, 34.0522, -118.2437)).Shipping!;
        Assert.Equal((8, 61), (la.Zone, la.Charge));
    }

    /// <summary>Freight estimate: $250 + $0.35 per road mile (straight line × 1.3), between $250 and $1,500.</summary>
    private static int FreightEstimate(double lat1, double lon1, double lat2, double lon2)
    {
        var roadMiles = ShippingCalculator.Distance(lat1, lon1, lat2, lon2, "mi");
        return (int)Math.Clamp(Math.Round(250 + (decimal)roadMiles * 0.35m, 0, MidpointRounding.AwayFromZero), 250, 1500);
    }

    [Fact]
    public async Task TooBigForUsps_IsDeliveredByFreight_AtTheEstimatedUsAverageRate()
    {
        var listing = await UsListing(d => d.WeightLb = 90);

        var quote = (await Estimate(listing.Id)).Shipping!;
        Assert.True(quote.Available);
        Assert.Equal(("Freight", "Freight"), (quote.Service, quote.SizeClass));
        Assert.Null(quote.Zone);
        Assert.True(quote.EstimatedRate);
        Assert.False(quote.PlaceholderRates);
        // ~81 straight-line miles → ~105 road miles → $250 + $37 ≈ $287.
        Assert.Equal(FreightEstimate(NycLat, NycLon, PhillyLat, PhillyLon), quote.Charge);
    }

    [Fact]
    public async Task FreightOrder_IsMarkedAsAnEstimatedRate()
    {
        var listing = await UsListing(d => d.WeightLb = 90);
        var order = await Json<OrderDto>(s => s.Post.Json(new BuyListingDto { Quantity = 1 }).ToUrl($"/api/listings/{listing.Id}/buy"), "carol", "buyer");

        var shipped = await Json<OrderDto>(s => s.Post.Json(new ChooseDeliveryDto
        {
            Method = "Shipping",
            Address = new AddressDto { Name = "Carol Smith", Phone = "+12155550123", Line1 = "1500 Market St", City = "Philadelphia", Postcode = "19102", CountryCode = "US" },
            Latitude = PhillyLat, Longitude = PhillyLon,
        }).ToUrl($"/api/orders/{order.Id}/delivery-method"), "carol", "buyer");

        Assert.Equal("Freight", shipped.ShippingService);
        Assert.True(shipped.ShippingRateEstimated);
        Assert.Equal(120 + FreightEstimate(NycLat, NycLon, PhillyLat, PhillyLon), shipped.Total);
    }

    [Fact]
    public async Task UsFreightAndVehicleRates_AreSourcedEstimates_UntilAnAdminSetsCarrierRates()
    {
        var us = await Json<List<ShippingRateDto>>(s => s.Get.Url("/api/shipping/rates?country=US"), "carol", "buyer");
        var freight = us.Single(r => r.SizeClass == "Freight");
        var vehicle = us.Single(r => r.SizeClass == "Vehicle");
        // Up to 4,000 road miles: corner to corner (Miami → Seattle ≈ 3,550 mi).
        Assert.Equal((250m, 0.35m, 250m, 1500m, 4000), (freight.BaseFee, freight.RatePerUnit, freight.MinFee, freight.MaxFee, freight.MaxDistance));
        Assert.Equal((350m, 0.70m, 400m, 2800m, 4000), (vehicle.BaseFee, vehicle.RatePerUnit, vehicle.MinFee, vehicle.MaxFee, vehicle.MaxDistance));
        Assert.All([freight, vehicle], r => Assert.True(r.IsEstimate && !r.IsPlaceholder));
        Assert.Contains("Warp", freight.Source);
        Assert.Contains("Kelley Blue Book", vehicle.Source);

        // The lead picks a carrier: real rates replace the estimate.
        try
        {
            var real = await Json<ShippingRateDto>(s => s.Put.Json(new UpdateShippingRateDto
            {
                BaseFee = 275, RatePerUnit = 0.40m, MinFee = 275, MaxFee = 1600, MaxDistance = 3500, Source = "Carrier contract 2027",
            }).ToUrl("/api/shipping/rates/US/Freight"), "admin", "admin");
            Assert.False(real.IsEstimate);
            Assert.Equal("Carrier contract 2027", real.Source);

            var listing = await UsListing(d => d.WeightLb = 90);
            Assert.False((await Estimate(listing.Id)).Shipping!.EstimatedRate);
        }
        finally
        {
            // Back to the seeded estimate for the other tests.
            using var scope = fixture.Host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
            db.ShippingRates.RemoveRange(db.ShippingRates.Where(r => r.CountryCode == "US" && r.SizeClass == SizeClass.Freight));
            await db.SaveChangesAsync();
            ShippingRateSeed.Seed(db);
        }
    }

    [Fact]
    public async Task ChoosingShipping_FixesTheUspsChargeOnTheOrder()
    {
        var listing = await UsListing();
        var order = await Json<OrderDto>(s => s.Post.Json(new BuyListingDto { Quantity = 1 }).ToUrl($"/api/listings/{listing.Id}/buy"), "carol", "buyer");

        var shipped = await Json<OrderDto>(s => s.Post.Json(new ChooseDeliveryDto
        {
            Method = "Shipping",
            Address = new AddressDto { Name = "Carol Smith", Phone = "+12155550123", Line1 = "1500 Market St", City = "Philadelphia", Postcode = "19102", CountryCode = "US" },
            Latitude = PhillyLat, Longitude = PhillyLon,
        }).ToUrl($"/api/orders/{order.Id}/delivery-method"), "carol", "buyer");

        Assert.Equal(12, shipped.ShippingCharge);
        Assert.Equal(120 + 12, shipped.Total);
        Assert.Equal(("USPS Ground Advantage", 2), (shipped.ShippingService, shipped.ShippingZone));
    }
}
