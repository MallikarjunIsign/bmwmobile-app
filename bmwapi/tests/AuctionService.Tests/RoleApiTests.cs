using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using AuctionService.Controllers;
using AuctionService.Data;
using AuctionService.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuctionService.Tests;

/// <summary>
/// Buyer / seller / admin rules: only sellers create, edit, publish, cancel and relist listings or manage photos;
/// buyers can still buy; only the order's seller moves fulfilment steps. Refusals are 403 with a friendly message.
/// </summary>
[Collection("auction-service")]
public class RoleApiTests(AppFixture fixture) : IAsyncLifetime
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

    private async Task<Alba.IScenarioResult> Call(Action<Alba.Scenario> configure, string user, string role, HttpStatusCode status)
    {
        return await fixture.Host.Scenario(s =>
        {
            s.RemoveClaim("role");
            s.WithClaim(new Claim("role", role));
            s.WithClaim(new Claim("username", user));
            s.WithClaim(new Claim("name", $"{user} Kumar"));
            configure(s);
            s.StatusCodeShouldBe(status);
        });
    }

    private async Task<T> Seller<T>(Action<Alba.Scenario> configure, HttpStatusCode status = HttpStatusCode.OK) =>
        await (await Call(configure, "bob", "seller", status)).ReadAsJsonAsync<T>();

    private async Task<string> AsBuyer(Action<Alba.Scenario> configure, HttpStatusCode status, string user = "carol") =>
        await (await Call(configure, user, "buyer", status)).ReadAsTextAsync();

    private static MultipartFormDataContent Photos(int count)
    {
        var form = new MultipartFormDataContent();
        for (var i = 0; i < count; i++)
        {
            var content = new ByteArrayContent(TestImages.Jpeg());
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(content, "files", $"{i}.jpg");
        }
        return form;
    }

    private async Task<AuctionDto> LiveDashCam(string saleType, int quantity = 1)
    {
        int partTypeId;
        using (var scope = fixture.Host.Services.CreateScope())
            partTypeId = (await scope.ServiceProvider.GetRequiredService<AuctionDbContext>().PartTypes.SingleAsync(p => p.Name == "Dash cam")).Id;

        var draft = await Seller<AuctionDto>(s => s.Post.Json(new CreateListingDto
        {
            Kind = "CommonPart", PartTypeId = partTypeId, CountryCode = "IN",
            LocationName = "Kukatpally, Hyderabad", Latitude = 17.4849, Longitude = 78.4138
        }).ToUrl("/api/listings"), HttpStatusCode.Created);

        await Seller<AuctionDto>(s => s.Put.Json(new SaveListingDto
        {
            SaleType = saleType,
            PartDetails = new PartDetailsDto { BrandType = "Aftermarket", Brand = "70mai", Condition = "New", Quantity = quantity },
            PartPrice = 3500,
            DeliveryOptions = ["Pickup"]
        }).ToUrl($"/api/listings/{draft.Id}"));
        await Call(s => s.Post.MultipartFormData(Photos(3)).ToUrl($"/api/auctions/{draft.Id}/images"), "bob", "seller", HttpStatusCode.OK);
        return await Seller<AuctionDto>(s => s.Post.Json(new { }).ToUrl($"/api/listings/{draft.Id}/publish"));
    }

    [Fact]
    public async Task Buyer_CannotCreateAListing_GetsAFriendly403()
    {
        var body = await AsBuyer(s => s.Post.Json(new CreateListingDto { Kind = "CommonPart", CountryCode = "IN" }).ToUrl("/api/listings"),
            HttpStatusCode.Forbidden);

        Assert.Contains("SELLER_ONLY", body);
        Assert.Contains("Only seller accounts can list", body);
    }

    [Fact]
    public async Task Buyer_CannotUseTheOlderCreateAuctionEndpoint()
    {
        var body = await AsBuyer(s => s.Post.Json(new CreateAuctionDto
        {
            Make = "Ford", Model = "Focus", Color = "Blue", Mileage = 1000, Year = 2020, StartingPrice = 100,
            AuctionEnd = DateTime.UtcNow.AddDays(3)
        }).ToUrl("/api/auctions"), HttpStatusCode.Forbidden);

        Assert.Contains("SELLER_ONLY", body);
    }

    [Fact]
    public async Task Buyer_CannotEditPublishCancelRelistOrAddPhotos_EvenAsTheListingsOwner()
    {
        var live = await LiveDashCam("Auction");

        // bob downgraded to a buyer token: every change is refused.
        await AsBuyer(s => s.Put.Json(new SaveListingDto { PartPrice = 100 }).ToUrl($"/api/listings/{live.Id}"), HttpStatusCode.Forbidden, "bob");
        await AsBuyer(s => s.Post.Json(new { }).ToUrl($"/api/listings/{live.Id}/publish"), HttpStatusCode.Forbidden, "bob");
        await AsBuyer(s => s.Post.Json(new AddNoteDto { Text = "Box included" }).ToUrl($"/api/listings/{live.Id}/notes"), HttpStatusCode.Forbidden, "bob");
        await AsBuyer(s => s.Post.Json(new { }).ToUrl($"/api/listings/{live.Id}/cancel"), HttpStatusCode.Forbidden, "bob");
        await AsBuyer(s => s.Post.Json(new { }).ToUrl($"/api/listings/{live.Id}/relist"), HttpStatusCode.Forbidden, "bob");
        await AsBuyer(s => s.Post.MultipartFormData(Photos(1)).ToUrl($"/api/auctions/{live.Id}/images"), HttpStatusCode.Forbidden, "bob");

        // Nothing changed.
        var still = await Seller<AuctionDto>(s => s.Get.Url($"/api/listings/{live.Id}"));
        Assert.Equal("Live", still.Status);
    }

    [Fact]
    public async Task Buyer_CanBuyADirectSale_ButOnlyTheOrdersSellerMovesFulfilment()
    {
        var live = await LiveDashCam("FixedPrice", quantity: 2);

        var order = await (await Call(s => s.Post.Json(new BuyListingDto { Quantity = 1 }).ToUrl($"/api/listings/{live.Id}/buy"),
            "carol", "buyer", HttpStatusCode.OK)).ReadAsJsonAsync<OrderDto>();

        // The buyer can't mark their own purchase ready for pickup.
        var body = await AsBuyer(s => s.Post.Json(new OrderStatusDto { Status = "ReadyForPickup" }).ToUrl($"/api/orders/{order.Id}/status"),
            HttpStatusCode.Forbidden);
        Assert.Contains("Only", body);
        Assert.Contains("NOT_ORDER_PARTY", body);

        // Another seller can't either: to them the order doesn't exist.
        await Call(s => s.Post.Json(new OrderStatusDto { Status = "ReadyForPickup" }).ToUrl($"/api/orders/{order.Id}/status"),
            "dave", "seller", HttpStatusCode.NotFound);

        // The order's seller can.
        var ready = await Seller<OrderDto>(s => s.Post.Json(new OrderStatusDto { Status = "ReadyForPickup" }).ToUrl($"/api/orders/{order.Id}/status"));
        Assert.Equal("ReadyForPickup", ready.Status);
    }

    [Fact]
    public async Task Internal_SellerUsernames_ListsEveryoneWithListings()
    {
        await LiveDashCam("Auction");

        var result = await fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/internal/seller-usernames");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        Assert.Contains("bob", await result.ReadAsJsonAsync<List<string>>());
    }
}
