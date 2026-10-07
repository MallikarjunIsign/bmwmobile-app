using AuctionService.Data;
using AuctionService.Entities;
using Contracts;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Tracking;

namespace AuctionService.Tests;

[Collection("auction-service")]
public class AuctionExtendedHandlerTests(AppFixture fixture) : IAsyncLifetime
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

    private async Task<Auction> Reload()
    {
        using var scope = fixture.Host.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AuctionDbContext>().Auctions.FindAsync(_auction.Id))!;
    }

    [Fact]
    public async Task Handle_LaterEnd_MovesAuctionEnd()
    {
        var newEnd = _auction.AuctionEnd.AddMinutes(2);

        await fixture.Host.InvokeMessageAndWaitAsync(new AuctionExtended { AuctionId = _auction.Id, AuctionEnd = newEnd });

        Assert.Equal(newEnd, (await Reload()).AuctionEnd, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Handle_EarlierEnd_IsIgnored()
    {
        await fixture.Host.InvokeMessageAndWaitAsync(
            new AuctionExtended { AuctionId = _auction.Id, AuctionEnd = _auction.AuctionEnd.AddMinutes(-5) });

        Assert.Equal(_auction.AuctionEnd, (await Reload()).AuctionEnd, TimeSpan.FromMilliseconds(1));
    }
}
