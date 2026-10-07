using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace AuctionService.Tests;

[Collection("auction-service")]
public class RelistTests(AppFixture fixture) : IAsyncLifetime
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

    private readonly Auction _unsold = TestData.CreatePartAuction(x =>
    {
        x.Status = Status.Unsold;
        x.AuctionEnd = DateTime.UtcNow.AddDays(-1);
    });

    public async Task InitializeAsync()
    {
        using var scope = fixture.Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        dbContext.Auctions.Add(_unsold);
        await dbContext.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = fixture.Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        dbContext.Auctions.RemoveRange(dbContext.Auctions);
        await dbContext.SaveChangesAsync();
    }

    private static RelistAuctionDto Terms(Action<RelistAuctionDto>? configure = null)
    {
        var dto = new RelistAuctionDto
        {
            StartingPrice = 200,
            ReservePrice = 300,
            BidIncrement = 20,
            AuctionEnd = DateTime.UtcNow.AddDays(5)
        };
        configure?.Invoke(dto);
        return dto;
    }

    private async Task<string> UploadImageToSource()
    {
        // Images can only be uploaded while Live, so add one before the auction "ended".
        using (var scope = fixture.Host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
            (await dbContext.Auctions.FindAsync(_unsold.Id))!.Status = Status.Live;
            await dbContext.SaveChangesAsync();
        }

        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(PngBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "files", "part.png");

        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "seller"));
            s.Post.MultipartFormData(form).ToUrl($"/api/auctions/{_unsold.Id}/images");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        var images = await result.ReadAsJsonAsync<List<ItemImageDto>>();

        using (var scope = fixture.Host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
            (await dbContext.Auctions.FindAsync(_unsold.Id))!.Status = Status.Unsold;
            await dbContext.SaveChangesAsync();
        }

        return images.Last().Url;
    }

    [Fact]
    public async Task Relist_Unsold_CreatesLiveCopyWithNewTermsAndCopiedImages()
    {
        var uploadedUrl = await UploadImageToSource();

        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "seller"));
            s.Post.Json(Terms()).ToUrl($"/api/auctions/{_unsold.Id}/relist");
            s.StatusCodeShouldBe(HttpStatusCode.Created);
        });

        var relisted = await result.ReadAsJsonAsync<AuctionDto>();

        Assert.NotEqual(_unsold.Id, relisted.Id);
        Assert.Equal("Live", relisted.Status);
        Assert.Equal("Part", relisted.ItemType);
        Assert.Equal("Front bumper", relisted.PartName);
        Assert.Equal(2020, relisted.YearTo);
        Assert.Equal(200, relisted.StartingPrice);
        Assert.Equal(300, relisted.ReservePrice);
        Assert.Equal(20, relisted.BidIncrement);

        // the external seed-style image is reused, the uploaded file is copied under a new name
        Assert.Equal(2, relisted.Images.Count);
        Assert.Contains("https://image.url/car.jpg", relisted.Images);
        var copiedUrl = Assert.Single(relisted.Images, x => x.StartsWith("http://localhost:6001/images/"));
        Assert.NotEqual(uploadedUrl, copiedUrl);
        Assert.True(File.Exists(Path.Combine(fixture.UploadsPath, copiedUrl.Split('/').Last())));
    }

    [Fact]
    public async Task Relist_LiveAuction_Returns400()
    {
        var live = TestData.CreateAuction();
        using (var scope = fixture.Host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
            dbContext.Auctions.Add(live);
            await dbContext.SaveChangesAsync();
        }

        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "seller"));
            s.Post.Json(Terms()).ToUrl($"/api/auctions/{live.Id}/relist");
            s.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task Relist_ByOtherUser_Returns403()
    {
        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "alice"));
            s.Post.Json(Terms()).ToUrl($"/api/auctions/{_unsold.Id}/relist");
            s.StatusCodeShouldBe(HttpStatusCode.Forbidden);
        });
    }

    [Fact]
    public async Task Relist_WithInvalidTerms_Returns400()
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "seller"));
            s.Post.Json(Terms(x => x.ReservePrice = 100)).ToUrl($"/api/auctions/{_unsold.Id}/relist");
            s.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });

        Assert.Contains("\"reservePrice\"", await result.ReadAsTextAsync());
    }
}