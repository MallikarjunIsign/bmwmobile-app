using IdentityService.Contracts;
using IdentityService.Infrastructure;
using IdentityService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static Duende.IdentityServer.IdentityServerConstants;

namespace IdentityService.Controllers;

/// <summary>
/// Admin-only seller management (no admin screens yet: use Swagger or IdentityService.http).
/// Needs an access token with the IdentityServerApi scope whose "role" is admin; anyone else gets 403.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(LocalApi.PolicyName)]
[AdminOnly]
[TypeFilter(typeof(ApiExceptionFilter))]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
public class AdminSellersController(AdminSellerService admin) : ControllerBase
{
    /// <summary>Create a seller and email them a one-time code to set their password.</summary>
    [HttpPost("sellers")]
    [ProducesResponseType(typeof(SellerInviteResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateSeller(CreateSellerRequest req, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await admin.CreateSellerAsync(req, ct));

    /// <summary>Send the "set your password" code again (only before the seller has set a password).</summary>
    [HttpPost("sellers/{id}/invite")]
    [ProducesResponseType(typeof(SellerInviteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<SellerInviteResponse>> ResendInvite(string id, CancellationToken ct) =>
        Ok(await admin.ResendInviteAsync(id, ct));

    /// <summary>Update details, or disable / enable the seller ("disabled": true / false).</summary>
    [HttpPatch("sellers/{id}")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDto>> UpdateSeller(string id, UpdateSellerRequest req, CancellationToken ct) =>
        Ok(await admin.UpdateSellerAsync(id, req, ct));

    /// <summary>Sellers, newest first. search matches name, email, username or business name.</summary>
    [HttpGet("sellers")]
    [ProducesResponseType(typeof(PagedResponse<AdminUserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<AdminUserDto>>> ListSellers(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await admin.ListSellersAsync(search, page, pageSize, ct));

    /// <summary>All accounts with their role, e.g. to find a buyer's id before make-seller.</summary>
    [HttpGet("users")]
    [ProducesResponseType(typeof(PagedResponse<AdminUserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<AdminUserDto>>> ListUsers(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await admin.ListUsersAsync(search, page, pageSize, ct));

    /// <summary>Upgrade an existing buyer to seller. The app picks up the new role at its next token refresh.</summary>
    [HttpPost("users/{id}/make-seller")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDto>> MakeSeller(string id, CancellationToken ct) =>
        Ok(await admin.MakeSellerAsync(id, ct));
}
