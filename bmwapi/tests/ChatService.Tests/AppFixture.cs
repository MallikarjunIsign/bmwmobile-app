using System.Collections.Concurrent;
using Alba;
using Alba.Security;
using ChatService.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Wolverine;

namespace ChatService.Tests;

/// <summary>Listings "in AuctionService" for the tests (instead of HTTP).</summary>
public class FakeListingSource : IListingSource
{
    public ConcurrentDictionary<string, ListingInfo> Listings { get; } = new();

    public Task<ListingInfo?> GetAsync(string listingId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Listings.GetValueOrDefault(listingId));
}

/// <summary>Records pushes instead of calling Firebase; tokens starting with "dead" are reported invalid.</summary>
public class FakePushSender : IPushSender
{
    public ConcurrentQueue<(IReadOnlyList<string> Tokens, PushMessage Message)> Sent { get; } = new();

    public Task<IReadOnlyList<string>> SendAsync(IReadOnlyList<string> tokens, PushMessage message, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue((tokens, message));
        return Task.FromResult<IReadOnlyList<string>>(tokens.Where(t => t.StartsWith("dead")).ToList());
    }
}

public class AppFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgreSqlContainer =
        new PostgreSqlBuilder("postgres:18")
            .WithDatabase("chat")
            .WithUsername("postgres")
            .WithPassword("postgrespw")
            .Build();

    public IAlbaHost Host { get; set; } = null!;
    public FakeListingSource Listings { get; } = new();
    public FakePushSender Push { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgreSqlContainer.StartAsync();

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("ConnectionStrings:DefaultConnection", _postgreSqlContainer.GetConnectionString());
            builder.UseSetting("Chat:MessagesPerMinute", "5");
            builder.UseSetting("Chat:PhotoBaseUrl", "http://gateway.test");
            builder.UseSetting("Admin:Usernames:0", "admin");
            builder.ConfigureServices(services =>
            {
                services.RunWolverineInSoloMode();
                services.DisableAllExternalWolverineTransports();
                services.RemoveAll<IListingSource>();
                services.AddSingleton<IListingSource>(Listings);
                services.RemoveAll<IPushSender>();
                services.AddSingleton<IPushSender>(Push);
            });
        }, new JwtSecurityStub());
    }

    public async Task DisposeAsync()
    {
        await Host.StopAsync();
        await Host.DisposeAsync();
        await _postgreSqlContainer.DisposeAsync();
    }
}

[CollectionDefinition("chat-service")]
public class ChatServiceCollection : ICollectionFixture<AppFixture>;
