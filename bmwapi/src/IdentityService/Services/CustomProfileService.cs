using System.Globalization;
using System.Security.Claims;
using IdentityService.Data;
using Microsoft.EntityFrameworkCore;
using IdentityModel;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using IdentityService.Models;
using Microsoft.AspNetCore.Identity;

namespace IdentityService.Services;

public class CustomProfileService(
    UserManager<ApplicationUser> userManager,
    AccountService accounts,
    ApplicationDbContext db) : IProfileService
{
    public async Task GetProfileDataAsync(ProfileDataRequestContext context)
    {
        var user = await userManager.GetUserAsync(context.Subject)
            ?? throw new InvalidOperationException("User not found");

        var existingClaims = await userManager.GetClaimsAsync(user);

        var claims = new List<Claim>
        {
            new("username", user.UserName ?? string.Empty),
            new(JwtClaimTypes.Email, user.Email ?? string.Empty),
        };

        context.IssuedClaims.AddRange(claims);

        var nameClaim = existingClaims.FirstOrDefault(c => c.Type == JwtClaimTypes.Name);
        if (nameClaim != null)
        {
            context.IssuedClaims.Add(nameClaim);
        }
        else if (!string.IsNullOrWhiteSpace(user.FullName))
        {
            context.IssuedClaims.Add(new Claim(JwtClaimTypes.Name, user.FullName));
        }

        // Primary location: AuctionService uses it as the default location of new listings.
        var primary = await db.UserLocations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.UserId == user.Id && l.IsPrimary);
        if (primary is not null)
        {
            context.IssuedClaims.Add(new Claim("location_name", primary.PlaceName));
            context.IssuedClaims.Add(new Claim("location_lat", primary.Latitude.ToString(CultureInfo.InvariantCulture)));
            context.IssuedClaims.Add(new Claim("location_lon", primary.Longitude.ToString(CultureInfo.InvariantCulture)));
        }

        // Customer / Service Pro, only while Features:RolesEnabled is on.
        if (accounts.RolesEnabled)
        {
            context.IssuedClaims.Add(new Claim(JwtClaimTypes.Role, await accounts.GetRoleAsync(user)));
        }
    }

    public Task IsActiveAsync(IsActiveContext context)
    {
        return Task.CompletedTask;
    }
}
