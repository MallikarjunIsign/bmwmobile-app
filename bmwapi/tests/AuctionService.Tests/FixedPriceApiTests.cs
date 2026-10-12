using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using AuctionService.Controllers;
using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Services;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Tracking;

namespace AuctionService.Tests;

/// <summary>Fixed-price ("Sell") listings, the seller-chosen auction end and the seller's name on listings.</summary>
[Collection("auction-service")]
public class FixedPriceApiTests(AppFixture fixture) : IAsyncLifetime
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

    private async Task<T> Send<T>(Action<Alba.Scenario> configure, string user = "bob", HttpStatusCode status = HttpStatusCode.OK)
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", user));
            s.WithClaim(new Claim("email", $"{user}@example.com"));
            s.WithClaim(new Claim("name", $"{char.ToUpperInvariant(user[0])}{user[1..]} Kumar"));
            configure(s);
            s.StatusCodeShouldBe(status);
        });
        return await result.ReadAsJsonAsync<T>();
    }

    private async Task<string> SendText(Action<Alba.Scenario> configure, HttpStatusCode status, string user = "bob")
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", user));
            configure(s);
            s.StatusCodeShouldBe(status);
        });
        return await result.ReadAsTextAsync();
    }

    private async Task<AuctionDto> DashCamDraft(Action<SaveListingDto> configure)
    {
        int partTypeId;
        using (var scope = fixture.Host.Services.CreateScope())
            partTypeId = (await scope.ServiceProvider.GetRequiredService<AuctionDbContext>().PartTypes.SingleAsync(p => p.Name == "Dash cam")).Id;

        var draft = await Send<AuctionDto>(s => s.Post.Json(new CreateListingDto
        {
            Kind = "CommonPart", PartTypeId = partTypeId, CountryCode = "IN",
            LocationName = "Kukatpally, Hyderabad", Latitude = 17.4849, Longitude = 78.4138
        }).ToUrl("/api/listings"), status: HttpStatusCode.Created);

        var dto = new SaveListingDto
        {
            PartDetails = new PartDetailsDto { BrandType = "Aftermarket", Brand = "70mai", Condition = "New", Quantity = 1 },
            PartPrice = 3500,
            DeliveryOptions = ["Pickup"]
        };
        configure(dto);
        await Send<AuctionDto>(s => s.Put.Json(dto).ToUrl($"/api/listings/{draft.Id}"));

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
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        return draft;
    }

    private Task<AuctionDto> Publish(string id) => Send<AuctionDto>(s => s.Post.Json(new { }).ToUrl($"/api/listings/{id}/publish"));

    private Task<OrderDto> Buy(string id, int quantity, string user) =>
        Send<OrderDto>(s => s.Post.Json(new BuyListingDto { Quantity = quantity }).ToUrl($"/api/listings/{id}/buy"), user);

    [Fact]
    public async Task Auction_EndsWhenTheSellerChose_AndShowsTheSellerName()
    {
        var end = DateTime.UtcNow.AddDays(2).AddHours(5);
        var draft = await DashCamDraft(d => d.AuctionEnd = end);
        Assert.Equal("Bob Kumar", draft.SellerName);

        var live = await Publish(draft.Id);

        Assert.Equal("Auction", live.SaleType);
        Assert.Equal(end, live.AuctionEnd, TimeSpan.FromSeconds(1));
        Assert.Null(live.DurationDays);
    }

    [Fact]
    public async Task Auction_EndTooSoon_IsRejected()
    {
        var draft = await DashCamDraft(_ => { });

        var body = await SendText(s => s.Put.Json(new SaveListingDto { AuctionEnd = DateTime.UtcNow.AddMinutes(20) })
            .ToUrl($"/api/listings/{draft.Id}"), HttpStatusCode.BadRequest);

        Assert.Contains("auctionEnd", body);
    }

    [Fact]
    public async Task FixedPrice_BuyersTakeUnitsUntilSoldOut_EachPurchaseIsAnOrder()
    {
        var draft = await DashCamDraft(d =>
        {
            d.SaleType = "FixedPrice";
            d.PartDetails!.Quantity = 4;
        });

        AuctionDto? live = null;
        var published = await fixture.Host.ExecuteAndWaitAsync(async () => live = await Publish(draft.Id));
        Assert.Equal("FixedPrice", live!.SaleType);
        Assert.Equal(ListingRules.NoEnd, live.AuctionEnd);
        Assert.Equal(4, live.QuantityAvailable);
        Assert.Equal("FixedPrice", published.Sent.SingleMessage<AuctionCreated>().SaleType);

        // The seller can't buy; nobody can bid-style buy more than what's left.
        await SendText(s => s.Post.Json(new BuyListingDto { Quantity = 1 }).ToUrl($"/api/listings/{draft.Id}/buy"), HttpStatusCode.BadRequest);

        OrderDto? first = null;
        var bought = await fixture.Host.ExecuteAndWaitAsync(async () => first = await Buy(draft.Id, 3, "alice"));
        Assert.Equal(3, first!.Quantity);
        Assert.Equal(10500, first.FinalTotal);
        Assert.Equal("Sold", first.StatusLabel);
        Assert.Equal("bob", bought.Sent.SingleMessage<OrderStatusChanged>().NotifyUser);
        Assert.Equal(1, bought.Sent.SingleMessage<AuctionUpdated>().QuantityAvailable);

        var tooMany = await SendText(s => s.Post.Json(new BuyListingDto { Quantity = 2 }).ToUrl($"/api/listings/{draft.Id}/buy"),
            HttpStatusCode.BadRequest, "carol");
        Assert.Contains("Only 1 left", tooMany);

        await Buy(draft.Id, 1, "carol");
        var sold = await Send<AuctionDto>(s => s.Get.Url($"/api/listings/{draft.Id}"));
        Assert.Equal("Sold", sold.Status);
        Assert.Equal(0, sold.QuantityAvailable);
        await SendText(s => s.Post.Json(new BuyListingDto { Quantity = 1 }).ToUrl($"/api/listings/{draft.Id}/buy"),
            HttpStatusCode.BadRequest, "dave");

        var orders = await Send<List<OrderDto>>(s => s.Get.Url("/api/orders"));
        Assert.Equal(2, orders.Count(o => o.AuctionId == draft.Id));
        var mine = await Send<List<MyListingDto>>(s => s.Get.Url("/api/listings/mine"));
        Assert.Equal("Sold", mine.Single(x => x.Id == draft.Id).State);
    }

    [Fact]
    public async Task Auctions_CantBeBoughtAtAFixedPrice()
    {
        var draft = await DashCamDraft(d => d.AuctionEnd = DateTime.UtcNow.AddDays(1));
        await Publish(draft.Id);

        await SendText(s => s.Post.Json(new BuyListingDto { Quantity = 1 }).ToUrl($"/api/listings/{draft.Id}/buy"),
            HttpStatusCode.BadRequest, "alice");
    }

    [Fact]
    public async Task CommonParts_OnlyOfferPartTypesThatFitAnyVehicle()
    {
        var types = await Send<List<PartTypeDto>>(s => s.Get.Url("/api/catalog/part-types?q=b&commonOnly=true&limit=200"));
        Assert.NotEmpty(types);
        Assert.DoesNotContain(types, t => t.FitmentMode == "VehicleSpecific");

        var categories = await Send<List<CategoryDto>>(s => s.Get.Url("/api/catalog/categories?commonOnly=true"));
        IEnumerable<PartTypeDto> All(CategoryDto c) => c.PartTypes.Concat(c.SubCategories.SelectMany(All));
        Assert.All(categories, c => Assert.NotEmpty(All(c)));
        Assert.DoesNotContain(categories.SelectMany(All), t => t.FitmentMode == "VehicleSpecific");
    }
}
