using System.Security.Claims;
using IdentityModel;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using IdentityService.Models;
using Microsoft.AspNetCore.Identity;

namespace IdentityService.Services;

public class CustomProfileService(UserManager<ApplicationUser> userManager, AccountService accounts) : IProfileService
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
