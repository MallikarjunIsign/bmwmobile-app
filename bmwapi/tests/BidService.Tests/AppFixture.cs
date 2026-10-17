using Alba;
using Alba.Security;
using BidService.Models;
using BidService.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;
using Wolverine;

namespace BidService.Tests;

public class AppFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgreSqlContainer =
        new PostgreSqlBuilder("postgres:18")
            .WithDatabase("bids")
            .WithUsername("postgres")
            .WithPassword("postgrespw")
            .Build();

    public IAlbaHost Host { get; private set; } = null!;

    /// <summary>The clock BidService uses; tests move it forward to end auctions.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public async Task InitializeAsync()
    {
        await _postgreSqlContainer.StartAsync();

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("ConnectionStrings:BidDbConnection", _postgreSqlContainer.GetConnectionString());
            builder.UseSetting("GrpcAuctionServiceUrl", "http://localhost:1");
            builder.ConfigureServices(services =>
            {
                services.RunWolverineInSoloMode();
                services.DisableAllExternalWolverineTransports();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.RemoveAll<GrpcAuctionClient>();
                services.AddScoped<GrpcAuctionClient, NoGrpcAuctionClient>();
            });
        }, // Every test user is a seller unless a test removes the "role" claim (buyer tests).
        new JwtSecurityStub().With(new System.Security.Claims.Claim("role", "seller")));
    }

    public async Task DisposeAsync()
    {
        await Host.StopAsync();
        await Host.DisposeAsync();
        await _postgreSqlContainer.DisposeAsync();
    }

    /// <summary>Tests create their auctions directly; nothing is fetched from AuctionService.</summary>
    private class NoGrpcAuctionClient(IConfiguration config) : GrpcAuctionClient(config)
    {
        public override Auction? GetAuction(string auctionId) => null;

        // Non-payment strikes (AuctionService): a test puts a bidder here to block them.
        public override string? BiddingBlockedReason(string bidder, Microsoft.Extensions.Logging.ILogger? logger = null) => BlockedBidders.GetValueOrDefault(bidder);
    }

    /// <summary>Bidders AuctionService would block (too many unpaid wins), with the reason.</summary>
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> BlockedBidders = new();
}

[CollectionDefinition("bid-service")]
public class BidServiceCollection : ICollectionFixture<AppFixture>;