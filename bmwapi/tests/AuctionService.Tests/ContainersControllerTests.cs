using System.Net;
using System.Security.Claims;
using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuctionService.Tests;

[Collection("auction-service")]
public class ContainersControllerTests(AppFixture fixture) : IAsyncLifetime
{
    private readonly Container _container = new()
    {
        ContainerNumber = $"TEST{Random.Shared.Next(1000000, 9999999)}",
        OriginPort = "Jebel Ali",
        OriginCountry = "United Arab Emirates",
        DestinationPort = "Nhava Sheva",
        DestinationCountry = "India",
        DepartureDate = DateTime.UtcNow.AddDays(-2),
        EstimatedArrivalDate = DateTime.UtcNow.AddDays(10),
        ShippingStatus = ShippingStatus.InTransit,
        CreatedBy = "bob"
    };

    public async Task InitializeAsync()
    {
        using var scope = fixture.Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        dbContext.Containers.Add(_container);
        await dbContext.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = fixture.Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        dbContext.Auctions.RemoveRange(dbContext.Auctions);
        await dbContext.SaveChangesAsync();
        dbContext.Containers.RemoveRange(dbContext.Containers);
        await dbContext.SaveChangesAsync();
    }

    private static SaveContainerDto NewDto(Action<SaveContainerDto>? configure = null)
    {
        var dto = new SaveContainerDto
        {
            ContainerNumber = $"mscu {Random.Shared.Next(1000000, 9999999)}",
            OriginPort = "Yokohama",
            OriginCountry = "Japan",
            DestinationPort = "Durban",
            DestinationCountry = "South Africa",
            DepartureDate = DateTime.UtcNow.AddDays(3),
            EstimatedArrivalDate = DateTime.UtcNow.AddDays(30),
            ShippingStatus = ShippingStatus.Loading
        };
        configure?.Invoke(dto);
        return dto;
    }

    private async Task LinkAuctionAsync()
    {
        using var scope = fixture.Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        dbContext.Auctions.Add(TestData.CreatePartAuction(x => x.Item.ContainerId = _container.Id));
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task GetContainers_Anonymous_ReturnsListWithAuctionCount()
    {
        await LinkAuctionAsync();

        var result = await fixture.Host.Scenario(s =>
        {
            s.RemoveRequestHeader("Authorization");
            s.Get.Url("/api/containers");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var containers = await result.ReadAsJsonAsync<List<ContainerDto>>();
        var dto = Assert.Single(containers, x => x.Id == _container.Id);
        Assert.Equal(1, dto.AuctionCount);
        Assert.Equal("InTransit", dto.ShippingStatus);
    }

    [Fact]
    public async Task GetContainer_IncludesAuctionsWithoutReserve()
    {
        await LinkAuctionAsync();

        var result = await fixture.Host.Scenario(s =>
        {
            s.RemoveRequestHeader("Authorization");
            s.Get.Url($"/api/containers/{_container.Id}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var json = await result.ReadAsTextAsync();
        Assert.DoesNotContain("reservePrice", json, StringComparison.OrdinalIgnoreCase);

        var dto = await result.ReadAsJsonAsync<ContainerDetailsDto>();
        var auction = Assert.Single(dto.Auctions);
        Assert.Equal(_container.ContainerNumber, auction.Container!.ContainerNumber);
    }

    [Fact]
    public async Task CreateContainer_Valid_NormalizesNumberAndSetsCreator()
    {
        var dto = NewDto();

        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "alice"));
            s.Post.Json(dto).ToUrl("/api/containers");
            s.StatusCodeShouldBe(HttpStatusCode.Created);
        });

        var created = await result.ReadAsJsonAsync<ContainerDto>();
        Assert.Equal(dto.ContainerNumber.Replace(" ", "").ToUpperInvariant(), created.ContainerNumber);
        Assert.Equal("alice", created.CreatedBy);
    }

    [Fact]
    public async Task CreateContainer_WithoutAuth_Returns401()
    {
        await fixture.Host.Scenario(s =>
        {
            s.RemoveRequestHeader("Authorization");
            s.Post.Json(NewDto()).ToUrl("/api/containers");
            s.StatusCodeShouldBe(HttpStatusCode.Unauthorized);
        });
    }

    [Theory]
    [InlineData("containerNumber")]
    [InlineData("estimatedArrivalDate")]
    [InlineData("destinationCountry")]
    public async Task CreateContainer_Invalid_Returns400(string field)
    {
        var dto = NewDto(x =>
        {
            switch (field)
            {
                case "containerNumber": x.ContainerNumber = "ABC123"; break;
                case "estimatedArrivalDate": x.EstimatedArrivalDate = x.DepartureDate.AddDays(-1); break;
                case "destinationCountry": x.DestinationCountry = ""; break;
            }
        });

        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "alice"));
            s.Post.Json(dto).ToUrl("/api/containers");
            s.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });

        Assert.Contains($"\"{field}\"", await result.ReadAsTextAsync());
    }

    [Fact]
    public async Task CreateContainer_ArrivalSameDayAsDeparture_IsAllowed()
    {
        var dto = NewDto(x => x.EstimatedArrivalDate = x.DepartureDate);

        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "alice"));
            s.Post.Json(dto).ToUrl("/api/containers");
            s.StatusCodeShouldBe(HttpStatusCode.Created);
        });
    }

    [Fact]
    public async Task CreateContainer_DuplicateNumber_Returns409()
    {
        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "alice"));
            s.Post.Json(NewDto(x => x.ContainerNumber = _container.ContainerNumber.ToLowerInvariant()))
                .ToUrl("/api/containers");
            s.StatusCodeShouldBe(HttpStatusCode.Conflict);
        });
    }

    [Fact]
    public async Task UpdateContainer_ByCreator_UpdatesAndBumpsLinkedAuctions()
    {
        await LinkAuctionAsync();
        var before = DateTime.UtcNow;
        var dto = NewDto(x =>
        {
            x.ContainerNumber = _container.ContainerNumber;
            x.ShippingStatus = ShippingStatus.Arrived;
        });

        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "bob"));
            s.Put.Json(dto).ToUrl($"/api/containers/{_container.Id}");
            s.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        using var scope = fixture.Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        var container = await dbContext.Containers.FindAsync(_container.Id);
        Assert.Equal(ShippingStatus.Arrived, container!.ShippingStatus);
        Assert.Equal("Durban", container.DestinationPort);

        var auction = await dbContext.Auctions.SingleAsync(x => x.Item.ContainerId == _container.Id);
        Assert.True(auction.UpdatedAt >= before);
    }

    [Fact]
    public async Task UpdateContainer_ByOtherUser_Returns403()
    {
        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "alice"));
            s.Put.Json(NewDto(x => x.ContainerNumber = _container.ContainerNumber)).ToUrl($"/api/containers/{_container.Id}");
            s.StatusCodeShouldBe(HttpStatusCode.Forbidden);
        });
    }

    [Fact]
    public async Task DeleteContainer_InUse_Returns409()
    {
        await LinkAuctionAsync();

        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "bob"));
            s.Delete.Url($"/api/containers/{_container.Id}");
            s.StatusCodeShouldBe(HttpStatusCode.Conflict);
        });
    }

    [Fact]
    public async Task DeleteContainer_Unused_ByCreator_Returns204()
    {
        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "bob"));
            s.Delete.Url($"/api/containers/{_container.Id}");
            s.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });
    }

    [Fact]
    public async Task CreateAuction_WithContainer_ReturnsContainerSummary()
    {
        var dto = TestData.BuildCreatePartDto(x => x.ContainerId = _container.Id);

        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "alice"));
            s.Post.Json(dto).ToUrl("/api/auctions");
            s.StatusCodeShouldBe(HttpStatusCode.Created);
        });

        var created = await result.ReadAsJsonAsync<AuctionDto>();
        Assert.Equal(_container.Id, created.ContainerId);
        Assert.Equal("Nhava Sheva", created.Container!.DestinationPort);
    }
}