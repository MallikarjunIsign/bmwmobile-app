using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuctionService.Tests;

[Collection("auction-service")]
public class AuctionImagesTests(AppFixture fixture) : IAsyncLifetime
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3];
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46, 0x49, 0x46, 0, 1];
    private static readonly byte[] WebpBytes = "RIFF\0\0\0\0WEBPVP8 "u8.ToArray();

    // An auction without images, like one that was just created.
    private readonly Auction _auction = TestData.CreateAuction(x => x.Item.Images.Clear());

    public async Task InitializeAsync()
    {
        using var scope = fixture.Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        dbContext.Auctions.Add(_auction);
        await dbContext.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = fixture.Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        dbContext.Auctions.RemoveRange(dbContext.Auctions);
        await dbContext.SaveChangesAsync();
    }

    private static MultipartFormDataContent Form(params (string Name, byte[] Bytes)[] files)
    {
        var form = new MultipartFormDataContent();
        foreach (var (name, bytes) in files)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(content, "files", name);
        }
        return form;
    }

    private async Task<List<ItemImageDto>> Upload(params (string Name, byte[] Bytes)[] files)
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "seller"));
            s.Post.MultipartFormData(Form(files)).ToUrl($"/api/auctions/{_auction.Id}/images");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        return await result.ReadAsJsonAsync<List<ItemImageDto>>();
    }

    private async Task<string> UploadExpecting(HttpStatusCode status, string user, params (string Name, byte[] Bytes)[] files)
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", user));
            s.Post.MultipartFormData(Form(files)).ToUrl($"/api/auctions/{_auction.Id}/images");
            s.StatusCodeShouldBe(status);
        });
        return await result.ReadAsTextAsync();
    }

    [Fact]
    public async Task Upload_ValidImages_StoresThemWithFirstAsPrimary()
    {
        var images = await Upload(("a.png", PngBytes), ("b.jpeg", JpegBytes), ("c.webp", WebpBytes));

        Assert.Equal(3, images.Count);
        Assert.Single(images, x => x.IsPrimary);
        Assert.True(images[0].IsPrimary);
        Assert.EndsWith(".png", images[0].Url);
        Assert.EndsWith(".jpg", images[1].Url); // stored with the detected extension
        Assert.EndsWith(".webp", images[2].Url);
        Assert.StartsWith("http://localhost:6001/images/", images[0].Url);
        Assert.All(images, x => Assert.True(File.Exists(Path.Combine(fixture.UploadsPath, x.Url.Split('/').Last()))));
    }

    [Fact]
    public async Task Upload_FileWithFakeImageContent_Returns400()
    {
        var body = await UploadExpecting(HttpStatusCode.BadRequest, "seller", ("evil.jpg", "<script>alert(1)</script>"u8.ToArray()));

        Assert.Contains("evil.jpg", body);
    }

    [Fact]
    public async Task Upload_DisallowedExtension_Returns400()
    {
        await UploadExpecting(HttpStatusCode.BadRequest, "seller", ("image.gif", PngBytes));
    }

    [Fact]
    public async Task Upload_FileTooLarge_Returns400()
    {
        var big = new byte[5 * 1024 * 1024 + 1];
        PngBytes.CopyTo(big, 0);

        var body = await UploadExpecting(HttpStatusCode.BadRequest, "seller", ("big.png", big));

        Assert.Contains("5 MB", body);
    }

    [Fact]
    public async Task Upload_MoreThanMaxFiles_Returns400()
    {
        await Upload(("first.png", PngBytes));

        var files = Enumerable.Range(0, 10).Select(i => ($"{i}.png", PngBytes)).ToArray();
        var body = await UploadExpecting(HttpStatusCode.BadRequest, "seller", files);

        Assert.Contains("at most 10", body);
    }

    [Fact]
    public async Task Upload_NotSeller_Returns403()
    {
        await UploadExpecting(HttpStatusCode.Forbidden, "someone-else", ("a.png", PngBytes));
    }

    [Fact]
    public async Task Upload_AuctionWithBids_Returns400()
    {
        using (var scope = fixture.Host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
            (await dbContext.Auctions.FindAsync(_auction.Id))!.CurrentHighBid = 100;
            await dbContext.SaveChangesAsync();
        }

        await UploadExpecting(HttpStatusCode.BadRequest, "seller", ("a.png", PngBytes));
    }

    [Fact]
    public async Task Delete_PrimaryImage_PromotesNextImageAndRemovesFile()
    {
        var images = await Upload(("a.png", PngBytes), ("b.png", PngBytes));
        var fileName = images[0].Url.Split('/').Last();

        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "seller"));
            s.Delete.Url($"/api/auctions/{_auction.Id}/images/{images[0].Id}");
            s.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        using var scope = fixture.Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        var remaining = await dbContext.ItemImages.Where(x => x.ItemId == _auction.Id).ToListAsync();

        var image = Assert.Single(remaining);
        Assert.Equal(images[1].Id, image.Id);
        Assert.True(image.IsPrimary);
        Assert.Equal(0, image.SortOrder);
        Assert.False(File.Exists(Path.Combine(fixture.UploadsPath, fileName)));
    }

    [Fact]
    public async Task Reorder_SetsOrderAndPrimary()
    {
        var images = await Upload(("a.png", PngBytes), ("b.png", PngBytes), ("c.png", PngBytes));
        var dto = new ReorderImagesDto
        {
            ImageIds = [images[2].Id, images[0].Id, images[1].Id],
            PrimaryImageId = images[0].Id
        };

        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "seller"));
            s.Put.Json(dto).ToUrl($"/api/auctions/{_auction.Id}/images/order");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var reordered = await result.ReadAsJsonAsync<List<ItemImageDto>>();

        // Primary first, then the remaining images in the requested order.
        Assert.Equal([images[0].Id, images[2].Id, images[1].Id], reordered.Select(x => x.Id));
        Assert.True(reordered[0].IsPrimary);
    }

    [Fact]
    public async Task Reorder_WithMissingIds_Returns400()
    {
        var images = await Upload(("a.png", PngBytes), ("b.png", PngBytes));

        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "seller"));
            s.Put.Json(new ReorderImagesDto { ImageIds = [images[0].Id] }).ToUrl($"/api/auctions/{_auction.Id}/images/order");
            s.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task GetImage_ServesFileWithContentTypeAndCacheHeaders()
    {
        var images = await Upload(("a.png", PngBytes));
        var path = new Uri(images[0].Url).AbsolutePath;

        var result = await fixture.Host.Scenario(s =>
        {
            s.RemoveRequestHeader("Authorization");
            s.Get.Url(path);
            s.StatusCodeShouldBe(HttpStatusCode.OK);
            s.Header("Content-Type").SingleValueShouldEqual("image/png");
            s.Header("Cache-Control").SingleValueShouldEqual("public, max-age=31536000, immutable");
            s.Header("X-Content-Type-Options").SingleValueShouldEqual("nosniff");
        });

        Assert.Equal(PngBytes.Length, result.Context.Response.ContentLength);
    }

    [Fact]
    public async Task GetAuction_ReturnsImagesWithPrimaryAsImageUrl()
    {
        var images = await Upload(("a.png", PngBytes), ("b.png", PngBytes));

        var result = await fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/auctions/{_auction.Id}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        var dto = await result.ReadAsJsonAsync<AuctionDto>();

        Assert.Equal(images[0].Url, dto.ImageUrl);
        Assert.Equal(images.Select(x => x.Url), dto.Images);
    }
}