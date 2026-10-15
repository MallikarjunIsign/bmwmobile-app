using System.Security.Claims;
using Contracts;
using Duende.IdentityServer.EntityFramework.DbContexts;
using IdentityModel;
using IdentityService.Data;
using IdentityService.Models;
using IdentityService.Options;
using IdentityService.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;

namespace IdentityService;

public class SeedData
{
    public static void EnsureSeedData(WebApplication app) => EnsureSeedData(app.Services, app.Configuration);

    /// <summary>Migrations, roles, test accounts and buyer roles for older accounts. Also called by the tests.</summary>
    public static void EnsureSeedData(IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate();

        // Refresh tokens and other IdentityServer grants.
        scope.ServiceProvider.GetRequiredService<PersistedGrantDbContext>().Database.Migrate();

        var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        // Marketplace roles: always there. Customer / Service Pro only while Features:RolesEnabled is on.
        var roles = AppRoles.All.ToList();
        if (configuration.GetValue<bool>("Features:RolesEnabled"))
            roles.AddRange([AccountService.RoleCustomer, AccountService.RolePro]);
        foreach (var role in roles)
        {
            if (!roleMgr.RoleExistsAsync(role).Result) roleMgr.CreateAsync(new IdentityRole(role)).Wait();
        }

        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (!userMgr.Users.Any())
        {
            CreateTestUser(userMgr, "alice", "AliceSmith@example.com", "Alice Smith");
            CreateTestUser(userMgr, "bob", "BobSmith@example.com", "Bob Smith");
        }

        // Admin and test seller for local testing: only when their email + password are set in user-secrets / env.
        var seed = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;
        EnsureAccount(userMgr, seed.AdminEmail, seed.AdminPassword, seed.AdminFullName, AppRoles.Admin, null, null, null, null);
        EnsureAccount(userMgr, seed.SellerEmail, seed.SellerPassword, seed.SellerFullName, AppRoles.Seller,
            seed.SellerCountryCode, seed.SellerCity, seed.SellerBusinessName, seed.SellerPhoneNumber);

        // Everyone without a marketplace role (accounts from before roles existed) is a buyer.
        // Users who already have listings are upgraded to seller by RoleBackfillService once AuctionService answers.
        var given = GiveMissingBuyerRoles(context, userMgr);
        if (given > 0) Log.Information("Gave the buyer role to {Count} existing accounts", given);
    }

    /// <summary>Adds the buyer role to every account that has none of buyer / seller / admin. Safe to run every start.</summary>
    public static int GiveMissingBuyerRoles(ApplicationDbContext context, UserManager<ApplicationUser> userMgr)
    {
        var marketplaceRoles = AppRoles.All.Select(r => r.ToUpperInvariant()).ToArray();
        var roleIds = context.Roles.Where(r => marketplaceRoles.Contains(r.NormalizedName!)).Select(r => r.Id);
        var withoutRole = context.Users
            .Where(u => !context.UserRoles.Any(ur => ur.UserId == u.Id && roleIds.Contains(ur.RoleId)))
            .Select(u => u.Id)
            .ToList();

        foreach (var id in withoutRole)
        {
            var user = userMgr.FindByIdAsync(id).Result!;
            userMgr.AddToRoleAsync(user, AppRoles.Buyer).Wait();
        }
        return withoutRole.Count;
    }

    private static void EnsureAccount(UserManager<ApplicationUser> userMgr, string? email, string? password, string fullName,
        string role, string? country, string? city, string? business, string? phone)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;

        email = AccountService.NormalizeEmail(email);
        var user = userMgr.FindByEmailAsync(email).Result;
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = UsernameGenerator.NextAvailableAsync(userMgr, fullName, CancellationToken.None).Result,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName,
                PhoneNumber = phone,
                CountryCode = country,
                City = city,
                BusinessName = business,
                CreatedAt = DateTime.UtcNow,
            };
            Check(userMgr.CreateAsync(user, password).Result);
            Check(userMgr.AddClaimAsync(user, new Claim(JwtClaimTypes.Name, fullName)).Result);
            // Never log the password.
            Log.Information("Seeded {Role} account {Username}", role, user.UserName);
        }

        if (!userMgr.IsInRoleAsync(user, role).Result) Check(userMgr.AddToRoleAsync(user, role).Result);
        if (role != AppRoles.Buyer && userMgr.IsInRoleAsync(user, AppRoles.Buyer).Result)
            Check(userMgr.RemoveFromRoleAsync(user, AppRoles.Buyer).Result);
    }

    private static void CreateTestUser(UserManager<ApplicationUser> userMgr, string username, string email, string name)
    {
        if (userMgr.FindByNameAsync(username).Result is not null) return;

        var user = new ApplicationUser { UserName = username, Email = email, EmailConfirmed = true };
        Check(userMgr.CreateAsync(user, "Pass123$").Result);
        Check(userMgr.AddClaimsAsync(user, [new Claim(JwtClaimTypes.Name, name)]).Result);
        Log.Debug("{Username} created", username);
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new Exception(result.Errors.First().Description);
    }
}
