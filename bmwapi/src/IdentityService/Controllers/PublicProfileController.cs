using IdentityService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Controllers;

/// <summary>What anyone may see about a seller: name, member since and city (from the primary location, never the address).</summary>
public record PublicProfileDto(string Username, string? DisplayName, DateTime? MemberSince, string? City, string? CountryCode);

/// <summary>Public seller profile data for the app's seller profile screen (ratings come from AuctionService).</summary>
[ApiController]
[AllowAnonymous]
[Route("api/users")]
[Produces("application/json")]
public class PublicProfileController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet("{username}/public")]
    public async Task<ActionResult<PublicProfileDto>> Get(string username)
    {
        var normalized = username.Trim().ToUpperInvariant();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedUserName == normalized);
        if (user is null) return NotFound();

        var primary = await db.UserLocations.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == user.Id && l.IsPrimary);
        // A GPS row named "Near Lagos" / "Current location" isn't a city to show.
        var city = primary?.PlaceName is { } place && !place.StartsWith("Near ") && place != "Current location" ? place : null;
        return new PublicProfileDto(user.UserName!, user.FullName, user.CreatedAt, city, primary?.CountryCode);
    }
}
