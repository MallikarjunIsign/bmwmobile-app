using System.Net;
using System.Security.Claims;
using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using AuctionService.Services;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Tracking;

namespace AuctionService.Tests;

/// <summary>Rules for "a new listing matches a buyer's open custom order".</summary>
public class CustomOrderMatcherTests
{
    // Hyderabad, and a point ~15 km away (Secunderabad side) / ~150 km away (Warangal).
    private const double HydLat = 17.3850, HydLon = 78.4867;

    private static PartRequest Order(Action<PartRequest>? configure = null)
    {
        var order = new PartRequest
        {
            User = "alice",
            Make = "Toyota",
            Model = "Hilux",
            Year = 2018,
            PartName = "Front bumper",
            Latitude = HydLat,
            Longitude = HydLon,
            RadiusKm = 60,
        };
        configure?.Invoke(order);
        return order;
    }

    private static Auction Listing(Action<Auction>? configure = null) => TestData.CreatePartAuction(x =>
    {
        x.Seller = "bob";
        x.Item.Latitude = 17.44;
        x.Item.Longitude = 78.50;
        configure?.Invoke(x);
    });

    [Fact]
    public void PartTypeAndVehicleWithinRadius_Matches()
    {
        Assert.True(CustomOrderMatcher.Matches(Order(), Listing(), out var km));
        Assert.InRange(km!.Value, 1, 15);
    }

    [Fact]
    public void YearOutsideTheFitmentRange_DoesNotMatch() =>
        Assert.False(CustomOrderMatcher.Matches(Order(x => x.Year = 2021), Listing(), out _));

    [Fact]
    public void OtherModel_DoesNotMatch() =>
        Assert.False(CustomOrderMatcher.Matches(Order(x => x.Model = "Corolla"), Listing(), out _));

    [Fact]
    public void OtherPartType_DoesNotMatch() =>
        Assert.False(CustomOrderMatcher.Matches(Order(x => x.PartName = "Rear bumper"), Listing(), out _));

    [Fact]
    public void AnyVehicle_MatchesOnPartTypeOnly() =>
        Assert.True(CustomOrderMatcher.Matches(Order(x => { x.Make = ""; x.Model = ""; x.Year = 0; }), Listing(), out _));

    [Fact]
    public void OutsideTheBuyersRadius_DoesNotMatch() =>
        Assert.False(CustomOrderMatcher.Matches(Order(), Listing(x => { x.Item.Latitude = 17.9689; x.Item.Longitude = 79.5941; }), out _));

    [Fact]
    public void ListingWithoutLocation_DoesNotMatchABuyerWithLocation() =>
        Assert.False(CustomOrderMatcher.Matches(Order(), Listing(x => { x.Item.Latitude = null; x.Item.Longitude = null; }), out _));

    [Fact]
    public void PartNumber_MatchesWhateverTheSpelling()
    {
        var order = Order(x => { x.PartName = ""; x.Make = ""; x.Model = ""; x.Year = 0; x.PartNumber = "52119 0k954"; });
        Assert.True(CustomOrderMatcher.Matches(order, Listing(x => x.Item.PartNumber = "52119-0K954"), out _));
    }

    [Fact]
    public void OwnListing_DoesNotMatch() =>
        Assert.False(CustomOrderMatcher.Matches(Order(x => x.User = "bob"), Listing(), out _));

    [Fact]
    public void ClosedOrder_DoesNotMatch() =>
        Assert.False(CustomOrderMatcher.Matches(Order(x => x.Status = CustomOrderStatus.Closed), Listing(), out _));

    [Fact]
    public void Vehicle_DoesNotMatchAPartOrder() =>
        Assert.False(CustomOrderMatcher.Matches(Order(), Listing(x => x.Item.ItemType = ItemType.Vehicle), out _));

    [Fact]
    public void PartNumbers_AreNormalised() => Assert.Equal("521190K954", PartNumbers.Normalize(" 52119-0k.954 "));
}

[Collection("auction-service")]
public class CustomOrderApiTests(AppFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        db.PartRequests.RemoveRange(db.PartRequests);
        db.Auctions.RemoveRange(db.Auctions);
        await db.SaveChangesAsync();
    }

    private async Task<PartRequestDto> CreateOrder(CreatePartRequestDto dto, string user = "alice")
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", user));
            s.Post.Json(dto).ToUrl("/api/partrequests");
            s.StatusCodeShouldBe(HttpStatusCode.Created);
        });
        return await result.ReadAsJsonAsync<PartRequestDto>();
    }

    private async Task<List<PartRequestDto>> MyOrders(string user = "alice")
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", user));
            s.Get.Url("/api/partrequests");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        return await result.ReadAsJsonAsync<List<PartRequestDto>>();
    }

    [Fact]
    public async Task CreateOrder_FromASearch_IsOpen()
    {
        var order = await CreateOrder(new CreatePartRequestDto
        {
            SearchText = "2016 hilux front bumper", PartName = "Front bumper", Make = "Toyota", Model = "Hilux", Year = 2016,
            Latitude = 17.385, Longitude = 78.4867, RadiusKm = 60, CountryCode = "in", LocationName = "Hyderabad"
        });

        Assert.Equal(CustomOrderStatus.Open, order.Status);
        Assert.Equal("IN", order.CountryCode);
        Assert.Equal("2016 hilux front bumper", order.SearchText);
    }

    [Fact]
    public async Task CreateOrder_WithNothingToLookFor_Returns400()
    {
        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "alice"));
            s.Post.Json(new CreatePartRequestDto { Make = "Toyota" }).ToUrl("/api/partrequests");
            s.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task Orders_ArePrivate()
    {
        await CreateOrder(new CreatePartRequestDto { PartName = "Headlamp" }, "alice");

        Assert.Empty(await MyOrders("carol"));
        Assert.Single(await MyOrders("alice"));
    }

    [Fact]
    public async Task Orders_RequireSignIn()
    {
        await fixture.Host.Scenario(s =>
        {
            s.RemoveRequestHeader("Authorization");
            s.Get.Url("/api/partrequests");
            s.StatusCodeShouldBe(HttpStatusCode.Unauthorized);
        });
    }

    [Fact]
    public async Task PublishingAMatchingListing_MarksTheOrderMatched_AndNotifiesTheBuyer()
    {
        var order = await CreateOrder(new CreatePartRequestDto
        {
            PartName = "Front bumper", Make = "Toyota", Model = "Hilux", Year = 2018,
            Latitude = 17.385, Longitude = 78.4867, RadiusKm = 60
        });
        var dto = TestData.BuildCreatePartDto(x => { x.Latitude = 17.44; x.Longitude = 78.50; x.LocationName = "Secunderabad"; });

        string? auctionId = null;
        var tracked = await fixture.Host.ExecuteAndWaitAsync(async () =>
        {
            var result = await fixture.Host.Scenario(s =>
            {
                s.WithClaim(new Claim("username", "bob"));
                s.Post.Json(dto).ToUrl("/api/auctions");
                s.StatusCodeShouldBe(HttpStatusCode.Created);
            });
            auctionId = (await result.ReadAsJsonAsync<AuctionDto>()).Id;
        });

        var message = tracked.Sent.SingleMessage<CustomOrderMatched>();
        Assert.Equal("alice", message.User);
        Assert.Equal(order.Id, message.OrderId);
        Assert.Equal(auctionId, message.AuctionId);

        var mine = Assert.Single(await MyOrders());
        Assert.Equal(CustomOrderStatus.Matched, mine.Status);
        Assert.Equal(auctionId, mine.MatchedAuctionId);
    }

    [Fact]
    public async Task ListingFarAway_LeavesTheOrderOpen()
    {
        await CreateOrder(new CreatePartRequestDto
        {
            PartName = "Front bumper", Make = "Toyota", Model = "Hilux", Latitude = 17.385, Longitude = 78.4867, RadiusKm = 60
        });

        // Lagos
        var dto = TestData.BuildCreatePartDto(x => { x.Latitude = 6.5244; x.Longitude = 3.3792; });
        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "bob"));
            s.Post.Json(dto).ToUrl("/api/auctions");
            s.StatusCodeShouldBe(HttpStatusCode.Created);
        });

        Assert.Equal(CustomOrderStatus.Open, Assert.Single(await MyOrders()).Status);
    }

    [Fact]
    public async Task CloseOrder_StopsMatching()
    {
        var order = await CreateOrder(new CreatePartRequestDto { PartName = "Front bumper" });

        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "alice"));
            s.Post.Json(new { }).ToUrl($"/api/partrequests/{order.Id}/close");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "bob"));
            s.Post.Json(TestData.BuildCreatePartDto()).ToUrl("/api/auctions");
            s.StatusCodeShouldBe(HttpStatusCode.Created);
        });

        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        Assert.Equal(CustomOrderStatus.Closed, (await db.PartRequests.SingleAsync(x => x.Id == order.Id)).Status);
    }
}
