using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Alba;
using AuctionService.Controllers;
using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Tracking;

namespace AuctionService.Tests;

/// <summary>Chat / review photo uploads, OrderCreated for the chat, and blocks (from chat) on direct sales.</summary>
[Collection("auction-service")]
public class MediaAndChatEventsTests(AppFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        db.Orders.RemoveRange(db.Orders);
        db.Auctions.RemoveRange(db.Auctions);
        db.MediaFiles.RemoveRange(db.MediaFiles);
        db.UserBlocks.RemoveRange(db.UserBlocks);
        await db.SaveChangesAsync();
    }

    private static MultipartFormDataContent Photos(int count, byte[]? data = null)
    {
        var form = new MultipartFormDataContent();
        for (var i = 0; i < count; i++)
        {
            var content = new ByteArrayContent(data ?? TestImages.Jpeg());
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(content, "files", $"{i}.jpg");
        }
        return form;
    }

    private Task<IScenarioResult> Upload(MultipartFormDataContent form, HttpStatusCode status, string purpose = "chat") =>
        fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "alice"));
            s.Post.MultipartFormData(form).ToUrl("/api/media").QueryString("purpose", purpose);
            s.StatusCodeShouldBe(status);
        });

    [Fact]
    public async Task Media_UpTo5Photos_CheckedAndStoredWithThumbnails_OwnedByTheUploader()
    {
        var result = await Upload(Photos(2), HttpStatusCode.OK);
        var media = await result.ReadAsJsonAsync<List<MediaDto>>();

        Assert.Equal(2, media.Count);
        Assert.All(media, m =>
        {
            Assert.Matches(@"/images/[0-9a-f]+\.jpg$", m.Url);
            Assert.NotNull(m.ThumbnailUrl);
        });
        using (var scope = fixture.Host.Services.CreateScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<AuctionDbContext>().MediaFiles.ToListAsync();
            Assert.All(stored, f => Assert.Equal(("alice", "chat"), (f.Owner, f.Purpose)));
        }

        await Upload(Photos(6), HttpStatusCode.BadRequest);
        await Upload(Photos(1, "not an image"u8.ToArray()), HttpStatusCode.BadRequest);
        await Upload(Photos(1), HttpStatusCode.BadRequest, purpose: "listing");
    }

    [Fact]
    public async Task DirectSale_ABuyerBlockedByTheSellerCantBuy_AndAPurchaseTellsTheChat()
    {
        // A direct sale by bob (fixed price, 2 units).
        string id;
        using (var scope = fixture.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
            id = Guid.NewGuid().ToString();
            db.Auctions.Add(new Auction
            {
                Id = id, Seller = "bob", Status = Status.Live, SaleType = SaleType.FixedPrice, StartingPrice = 500, ReservePrice = 500,
                AuctionEnd = DateTime.UtcNow.AddYears(10),
                Item = new Item { Id = id, Kind = ListingKind.CommonPart, ItemType = ItemType.Part, Make = "", Model = "", Description = "", Quantity = 2, DeliveryOptions = DeliveryOptions.Pickup }
            });
            await db.SaveChangesAsync();
        }

        await fixture.Host.InvokeMessageAndWaitAsync(new UserBlocked { Blocker = "bob", Blocked = "mallory" });
        var blocked = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "mallory"));
            s.Post.Json(new BuyListingDto { Quantity = 1 }).ToUrl($"/api/listings/{id}/buy");
            s.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
        Assert.Contains("can't buy from this seller", await blocked.ReadAsTextAsync());

        var tracked = await fixture.Host.ExecuteAndWaitAsync(() => fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "alice"));
            s.Post.Json(new BuyListingDto { Quantity = 1 }).ToUrl($"/api/listings/{id}/buy");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        }));
        var created = tracked.Sent.SingleMessage<OrderCreated>();
        Assert.Equal((id, "bob", "alice"), (created.AuctionId, created.Seller, created.Buyer));

        await fixture.Host.InvokeMessageAndWaitAsync(new UserUnblocked { Blocker = "bob", Blocked = "mallory" });
        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "mallory"));
            s.Post.Json(new BuyListingDto { Quantity = 1 }).ToUrl($"/api/listings/{id}/buy");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
    }

    [Fact]
    public async Task AuctionWon_TellsTheChatWhoBoughtIt()
    {
        string id;
        using (var scope = fixture.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
            id = Guid.NewGuid().ToString();
            db.Auctions.Add(new Auction
            {
                Id = id, Seller = "bob", Status = Status.Live, StartingPrice = 1000, ReservePrice = 1000, AuctionEnd = DateTime.UtcNow,
                Item = new Item { Id = id, Kind = ListingKind.CommonPart, ItemType = ItemType.Part, Make = "", Model = "", Description = "", DeliveryOptions = DeliveryOptions.Pickup }
            });
            await db.SaveChangesAsync();
        }

        var tracked = await fixture.Host.InvokeMessageAndWaitAsync(new AuctionFinished
        {
            AuctionId = id, Seller = "bob", Winner = "alice", Amount = 1200, ItemSold = true, Status = "Sold", EndReason = "TimeEnded"
        });

        var created = tracked.Sent.SingleMessage<OrderCreated>();
        Assert.Equal((id, "bob", "alice"), (created.AuctionId, created.Seller, created.Buyer));
    }
}
