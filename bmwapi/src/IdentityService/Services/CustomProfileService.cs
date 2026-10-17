using System.Globalization;
using System.Security.Claims;
using IdentityService.Data;
using Microsoft.EntityFrameworkCore;
using IdentityModel;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using IdentityService.Models;
using Microsoft.AspNetCore.Identity;
using Contracts;

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

        // Phone: shown only to the other party of a completed sale (AuctionService Orders).
        if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
            claims.Add(new Claim(JwtClaimTypes.PhoneNumber, user.PhoneNumber));

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
            if (!string.IsNullOrWhiteSpace(primary.CountryCode))
                context.IssuedClaims.Add(new Claim(PartRequestAudience.CountryClaim, primary.CountryCode));
        }
        else if (!string.IsNullOrWhiteSpace(user.CountryCode))
        {
            // A seller created by an admin, before they saved a location: their country.
            context.IssuedClaims.Add(new Claim(PartRequestAudience.CountryClaim, user.CountryCode));
        }

        // The account's own country: AuctionService opens a seller's payment account there.
        if (!string.IsNullOrWhiteSpace(user.CountryCode))
            context.IssuedClaims.Add(new Claim(AppRoles.AccountCountryClaim, user.CountryCode));

        // Marketplace role: buyer, seller or admin. Always issued; every service checks it (Contracts.AppRoles).
        // Refresh tokens re-read it (UpdateAccessTokenClaimsOnRefresh), so an upgrade to seller shows up on the next refresh.
        context.IssuedClaims.Add(new Claim(AppRoles.ClaimType, await accounts.GetRoleAsync(user)));
    }

    /// <summary>A disabled account gets no new tokens, including from its refresh token.</summary>
    public async Task IsActiveAsync(IsActiveContext context)
    {
        var user = await userManager.GetUserAsync(context.Subject);
        context.IsActive = user is { IsDisabled: false };
    }
}
