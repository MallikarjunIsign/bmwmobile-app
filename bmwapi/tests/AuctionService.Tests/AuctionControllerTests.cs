using System.Net;
using System.Security.Claims;
using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuctionService.Tests;

[Collection("auction-service")]
public class AuctionControllerTests(AppFixture fixture) : IAsyncLifetime
{
    private readonly Auction _auction = TestData.CreateAuction();

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

    [Fact]
    public async Task GetAuctions_WithNoParams_ReturnsListOfAuctions()
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/auctions");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var auctions = await result.ReadAsJsonAsync<List<AuctionDto>>();

        var dto = Assert.Single(auctions, x => x.Id == _auction.Id);
        Assert.Equal(_auction.Seller, dto.Seller);
        Assert.Equal(_auction.Item.Make, dto.Make);
        Assert.Equal(_auction.Item.Model, dto.Model);
        Assert.Equal(_auction.Status.ToString(), dto.Status);
    }

    [Fact]
    public async Task GetAuction_WithValidId_ReturnsAuction()
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/auctions/{_auction.Id}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var dto = await result.ReadAsJsonAsync<AuctionDto>();

        Assert.Equal(_auction.Seller, dto.Seller);
        Assert.Equal(_auction.Item.Make, dto.Make);
        Assert.Equal(_auction.Item.Model, dto.Model);
        Assert.Equal(_auction.Status.ToString(), dto.Status);
    }
    
    [Fact]
    public async Task GetAuction_WithInvalidId_Returns404()
    {
        await fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/auctions/fake");
            s.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });
    }
    
    [Fact]
    public async Task CreateAuction_WithoutAuth_Returns401()
    {
        var dto = TestData.BuildCreateAuctionDto();
        
        await fixture.Host.Scenario(s =>
        {
            s.RemoveRequestHeader("Authorization");
            s.Post.Json(dto).ToUrl("/api/auctions");
            s.StatusCodeShouldBe(HttpStatusCode.Unauthorized);
        });
    }
    
    [Fact]
    public async Task CreateAuction_WithAuth_ReturnsCreatedWithSellerSetFromUser()
    {
        var dto = TestData.BuildCreateAuctionDto();
        
        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "bob"));
            s.Post.Json(dto).ToUrl("/api/auctions");
            s.StatusCodeShouldBe(HttpStatusCode.Created);
        });
        
        var created = await result.ReadAsJsonAsync<AuctionDto>();

        Assert.Equal("bob", created.Seller);
        Assert.Equal(dto.Make, created.Make);
        Assert.Equal(dto.Model, created.Model);
    }
    
    [Fact]
    public async Task UpdateAuction_WithData_Returns204()
    {
        var updateDto = new UpdateAuctionDto{Model = "Updated"};
        
        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "seller"));
            s.Put.Json(updateDto).ToUrl($"/api/auctions/{_auction.Id}");
            s.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });
        
        using var scope = fixture.Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        
        var auction = await dbContext.Auctions
            .Include(x => x.Item)
            .FirstOrDefaultAsync(x => x.Id == _auction.Id);
        
        Assert.Equal("Updated", auction!.Item.Model);
    }
    
    [Fact]
    public async Task DeleteAuction_WithValidId_Returns204()
    {
        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "seller"));
            s.Delete.Url($"/api/auctions/{_auction.Id}");
            s.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });
        
        using var scope = fixture.Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        
        var auction = await dbContext.Auctions.FindAsync(_auction.Id);

        Assert.Null(auction);
    }

    [Fact]
    public async Task GetAuction_Anonymous_DoesNotReturnReservePrice()
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.RemoveRequestHeader("Authorization");
            s.Get.Url($"/api/auctions/{_auction.Id}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var json = await result.ReadAsTextAsync();

        Assert.DoesNotContain("reservePrice", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"reserveMet\":false", json);
    }

    [Fact]
    public async Task GetAuction_AsSeller_ReturnsReservePrice()
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "seller"));
            s.Get.Url($"/api/auctions/{_auction.Id}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var dto = await result.ReadAsJsonAsync<AuctionDto>();

        Assert.Equal(_auction.ReservePrice, dto.ReservePrice);
    }

    [Fact]
    public async Task GetAuctions_NeverReturnsReservePrice()
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "seller"));
            s.Get.Url("/api/auctions");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        Assert.DoesNotContain("reservePrice", await result.ReadAsTextAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAuction_Part_ReturnsCreatedWithPartFields()
    {
        var dto = TestData.BuildCreatePartDto();

        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "bob"));
            s.Post.Json(dto).ToUrl("/api/auctions");
            s.StatusCodeShouldBe(HttpStatusCode.Created);
        });

        var created = await result.ReadAsJsonAsync<AuctionDto>();

        Assert.Equal("Part", created.ItemType);
        Assert.Equal("Front bumper", created.PartName);
        Assert.Equal("Body", created.PartCategory);
        Assert.Equal(2020, created.YearTo);
        Assert.Null(created.Mileage);
        Assert.Equal(10000, created.ReservePrice); // the seller sees their own reserve
    }

    [Fact]
    public async Task CreateAuction_Vehicle_IgnoresPartFields()
    {
        var dto = TestData.BuildCreateAuctionDto(x => x.PartName = "Should be dropped");

        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "bob"));
            s.Post.Json(dto).ToUrl("/api/auctions");
            s.StatusCodeShouldBe(HttpStatusCode.Created);
        });

        var created = await result.ReadAsJsonAsync<AuctionDto>();

        Assert.Equal("Vehicle", created.ItemType);
        Assert.Null(created.PartName);
    }

    public static TheoryData<string, Action<CreateAuctionDto>> InvalidCreateDtos => new()
    {
        { "mileage", dto => dto.Mileage = null },
        { "color", dto => dto.Color = null },
        { "startingPrice", dto => dto.StartingPrice = 0 },
        { "bidIncrement", dto => dto.BidIncrement = 0 },
        { "reservePrice", dto => dto.ReservePrice = 4999 },
        { "auctionEnd", dto => dto.AuctionEnd = DateTime.UtcNow.AddMinutes(-1) },
        { "containerId", dto => dto.ContainerId = "missing-container" },
    };

    [Theory]
    [MemberData(nameof(InvalidCreateDtos))]
    public async Task CreateAuction_Vehicle_WithInvalidData_Returns400(string field, Action<CreateAuctionDto> breakDto)
    {
        var dto = TestData.BuildCreateAuctionDto(breakDto);

        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "bob"));
            s.Post.Json(dto).ToUrl("/api/auctions");
            s.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });

        Assert.Contains($"\"{field}\"", await result.ReadAsTextAsync());
    }

    public static TheoryData<string, Action<CreateAuctionDto>> InvalidPartDtos => new()
    {
        { "partName", dto => dto.PartName = " " },
        { "partCategory", dto => dto.PartCategory = null },
        { "condition", dto => dto.Condition = null },
        { "quantity", dto => dto.Quantity = 0 },
        { "yearTo", dto => dto.YearTo = 2010 },
    };

    [Theory]
    [MemberData(nameof(InvalidPartDtos))]
    public async Task CreateAuction_Part_WithInvalidData_Returns400(string field, Action<CreateAuctionDto> breakDto)
    {
        var dto = TestData.BuildCreatePartDto(breakDto);

        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "bob"));
            s.Post.Json(dto).ToUrl("/api/auctions");
            s.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });

        Assert.Contains($"\"{field}\"", await result.ReadAsTextAsync());
    }

    [Fact]
    public async Task UpdateAuction_WithBids_Returns400()
    {
        using (var scope = fixture.Host.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
            var auction = await dbContext.Auctions.FindAsync(_auction.Id);
            auction!.CurrentHighBid = 100;
            await dbContext.SaveChangesAsync();
        }

        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "seller"));
            s.Put.Json(new UpdateAuctionDto { StartingPrice = 1 }).ToUrl($"/api/auctions/{_auction.Id}");
            s.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task UpdateAuction_SwitchToPart_RequiresPartFields()
    {
        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "seller"));
            s.Put.Json(new UpdateAuctionDto { ItemType = ItemType.Part }).ToUrl($"/api/auctions/{_auction.Id}");
            s.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }
}