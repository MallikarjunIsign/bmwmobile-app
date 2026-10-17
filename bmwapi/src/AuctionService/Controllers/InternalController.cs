using AuctionService.Data;
using AuctionService.Payments;
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

    /// <summary>
    /// IdentityService, when an admin creates a seller or upgrades a buyer: opens the seller's payment account
    /// (US / IE). Elsewhere, or with payments off, nothing happens ("coming soon").
    /// </summary>
    [HttpPost("payment-accounts")]
    public async Task<IActionResult> EnsurePaymentAccount(NewSellerPaymentAccountRequest request,
        [FromServices] SellerPaymentService payments, CancellationToken ct)
    {
        try
        {
            var account = await payments.EnsureAsync(request.Seller, request.Country, request.Email, request.Name, ct);
            return Ok(new { created = account is not null, status = account?.Status.ToString() ?? "NotAvailable" });
        }
        catch (PaymentProviderException e)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = e.Message });
        }
    }
}

public record NewSellerPaymentAccountRequest(string Seller, string? Country, string? Email, string? Name = null);
