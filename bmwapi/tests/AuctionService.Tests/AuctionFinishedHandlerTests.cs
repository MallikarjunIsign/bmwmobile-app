using AuctionService.Data;
using AuctionService.Entities;
using Contracts;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Tracking;

namespace AuctionService.Tests;

[Collection("auction-service")]
public class AuctionFinishedHandlerTests(AppFixture fixture) : IAsyncLifetime
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

    private AuctionFinished Message(string status, bool itemSold, int? amount = null, string? winner = null,
        string endReason = "TimeEnded", DateTime? deadline = null) => new()
    {
        AuctionId = _auction.Id,
        Seller = "seller",
        ItemSold = itemSold,
        Winner = winner,
        Amount = amount,
        Status = status,
        EndReason = endReason,
        SellerDecisionDeadline = deadline
    };

    private async Task<Auction> Reload()
    {
        using var scope = fixture.Host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        return (await dbContext.Auctions.FindAsync(_auction.Id))!;
    }

    [Fact]
    public async Task Handle_WhenSold_SetsWinnerAmountAndSoldStatus()
    {
        await fixture.Host.InvokeMessageAndWaitAsync(Message("Sold", true, 150, "bob"));

        var auction = await Reload();

        Assert.Equal("bob", auction.Winner);
        Assert.Equal(150, auction.SoldAmount);
        Assert.Equal(Status.Sold, auction.Status);
        Assert.Equal(EndReason.TimeEnded, auction.EndReason);
    }

    [Fact]
    public async Task Handle_WhenReserveNotMet_SetsStatusAndDeadlineWithoutWinner()
    {
        var deadline = DateTime.UtcNow.AddHours(24);

        await fixture.Host.InvokeMessageAndWaitAsync(Message("ReserveNotMet", false, 50, "bob", deadline: deadline));

        var auction = await Reload();

        Assert.Equal(Status.ReserveNotMet, auction.Status);
        Assert.Null(auction.Winner);
        Assert.NotNull(auction.SellerDecisionDeadline);
    }

    [Fact]
    public async Task Handle_SecondMessageAfterReserveNotMet_MovesToSold()
    {
        await fixture.Host.InvokeMessageAndWaitAsync(
            Message("ReserveNotMet", false, 50, "bob", deadline: DateTime.UtcNow.AddHours(24)));
        await fixture.Host.InvokeMessageAndWaitAsync(
            Message("Sold", true, 50, "bob", endReason: "SellerAcceptedAfterEnd"));

        var auction = await Reload();

        Assert.Equal(Status.Sold, auction.Status);
        Assert.Equal("bob", auction.Winner);
        Assert.Equal(EndReason.SellerAcceptedAfterEnd, auction.EndReason);
        Assert.Null(auction.SellerDecisionDeadline);
    }

    [Fact]
    public async Task Handle_LateUnsoldAfterSold_IsIgnored()
    {
        await fixture.Host.InvokeMessageAndWaitAsync(Message("Sold", true, 150, "bob", endReason: "SellerAccepted"));
        await fixture.Host.InvokeMessageAndWaitAsync(Message("Unsold", false, endReason: "GracePeriodExpired"));

        var auction = await Reload();

        Assert.Equal(Status.Sold, auction.Status);
        Assert.Equal("bob", auction.Winner);
    }

    [Fact]
    public async Task Handle_DuplicateMessage_IsIdempotent()
    {
        var message = Message("Sold", true, 150, "bob");

        await fixture.Host.InvokeMessageAndWaitAsync(message);
        await fixture.Host.InvokeMessageAndWaitAsync(message);

        var auction = await Reload();

        Assert.Equal(Status.Sold, auction.Status);
        Assert.Equal(150, auction.SoldAmount);
    }

    [Fact]
    public async Task Handle_WhenAuctionNotFound_ThrowsInvalidOperationException()
    {
        var message = new AuctionFinished
        {
            AuctionId = Guid.NewGuid().ToString(),
            ItemSold = false,
            Seller = "seller",
            Amount = 50
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Host.InvokeMessageAndWaitAsync(message));
    }
}