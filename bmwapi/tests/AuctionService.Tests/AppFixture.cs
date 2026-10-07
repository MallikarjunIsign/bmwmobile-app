using Alba;
using Alba.Security;
using Microsoft.AspNetCore.Hosting;
using Testcontainers.PostgreSql;
using Wolverine;

namespace AuctionService.Tests;

public class AppFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgreSqlContainer = 
        new PostgreSqlBuilder("postgres:18")
            .WithDatabase("auctions")
            .WithUsername("postgres")
            .WithPassword("postgrespw")
            .Build();

    public IAlbaHost Host { get; set; } = null!;

    // Uploaded test images go to a temp folder, never into the source tree.
    public string UploadsPath { get; } = Path.Combine(Path.GetTempPath(), $"carsties-test-uploads-{Guid.NewGuid():N}");
    
    public async Task InitializeAsync()
    {
        await _postgreSqlContainer.StartAsync();

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                _postgreSqlContainer.GetConnectionString());
            builder.UseSetting("ImageStorage:LocalPath", UploadsPath);
            builder.ConfigureServices(services =>
            {
                services.RunWolverineInSoloMode();
                services.DisableAllExternalWolverineTransports();
            });
        }, new JwtSecurityStub());
    }

    public async Task DisposeAsync()
    {
        await Host.StopAsync();
        await Host.DisposeAsync();
        await _postgreSqlContainer.DisposeAsync();
        if (Directory.Exists(UploadsPath)) Directory.Delete(UploadsPath, recursive: true);
    }
}

[CollectionDefinition("auction-service")]
public class AuctionServiceCollection : ICollectionFixture<AppFixture>;