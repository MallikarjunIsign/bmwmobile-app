using AuctionService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Controllers;

/// <summary>
/// Service-to-service reads. Not routed by the gateway (only reachable inside the backend network).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/internal")]
[ApiExplorerSettings(IgnoreApi = true)]
public class InternalController(AuctionDbContext context) : ControllerBase
{
    /// <summary>
    /// Usernames of everyone who has created a listing (drafts included). IdentityService uses it once to make
    /// those existing users sellers when buyer / seller roles are introduced. Sellers are public on listings anyway.
    /// </summary>
    [HttpGet("seller-usernames")]
    public async Task<ActionResult<List<string>>> SellerUsernames() =>
        await context.Auctions.AsNoTracking().Select(a => a.Seller).Distinct().OrderBy(s => s).ToListAsync();
}
