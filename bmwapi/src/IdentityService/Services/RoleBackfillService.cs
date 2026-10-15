using System.Net.Http.Json;
using Contracts;
using IdentityService.Data;
using IdentityService.Models;
using IdentityService.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IdentityService.Services;

/// <summary>
/// One-time move to buyer / seller roles: accounts that already have listings become sellers.
/// The listings live in AuctionService, so it is asked for their sellers' usernames (retrying while it starts up).
/// Runs once: the result is recorded in DataMigrations.
/// </summary>
public class RoleBackfillService(
    IServiceScopeFactory scopes,
    IHttpClientFactory http,
    IOptions<RoleBackfillOptions> options,
    ILogger<RoleBackfillService> logger) : BackgroundService
{
    public const string MigrationName = "roles-v1-sellers-from-listings";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        if (string.IsNullOrWhiteSpace(opt.AuctionServiceUrl))
        {
            logger.LogInformation("RoleBackfill:AuctionServiceUrl is not set: existing users with listings are not upgraded to seller");
            return;
        }

        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (await db.DataMigrations.AnyAsync(m => m.Name == MigrationName, stoppingToken)) return;
        }

        for (var attempt = 1; attempt <= opt.MaxAttempts && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                var client = http.CreateClient(nameof(RoleBackfillService));
                var url = $"{opt.AuctionServiceUrl.TrimEnd('/')}/api/internal/seller-usernames";
                var usernames = await client.GetFromJsonAsync<string[]>(url, stoppingToken) ?? [];

                using var scope = scopes.CreateScope();
                var upgraded = await UpgradeAsync(scope.ServiceProvider, usernames, stoppingToken);
                logger.LogInformation("Role backfill: {Count} existing users with listings are now sellers", upgraded);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Role backfill: AuctionService not reachable yet (attempt {Attempt}/{Max})", attempt, opt.MaxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(opt.RetrySeconds), stoppingToken);
            }
        }
        logger.LogWarning("Role backfill gave up; it will try again at the next start");
    }

    /// <summary>Makes these usernames sellers (buyers only; admins are left alone) and records the migration.</summary>
    public static async Task<int> UpgradeAsync(IServiceProvider services, IEnumerable<string> usernames, CancellationToken ct)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var accounts = services.GetRequiredService<AccountService>();

        var upgraded = 0;
        foreach (var username in usernames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var user = await users.FindByNameAsync(username);
            if (user is null || await accounts.GetRoleAsync(user) != AppRoles.Buyer) continue;

            await users.AddToRoleAsync(user, AppRoles.Seller);
            await users.RemoveFromRoleAsync(user, AppRoles.Buyer);
            upgraded++;
        }

        db.DataMigrations.Add(new DataMigration { Name = MigrationName, AppliedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        return upgraded;
    }
}
