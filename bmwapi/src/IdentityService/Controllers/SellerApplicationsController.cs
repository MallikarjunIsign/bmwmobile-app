using IdentityService.Contracts;
using IdentityService.Infrastructure;
using IdentityService.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using static Duende.IdentityServer.IdentityServerConstants;

namespace IdentityService.Controllers;

/// <summary>"Want to sell?": the app's form. Works signed out (from Sign up) and signed in (Profile, linked to the account).</summary>
[ApiController]
[Route("api/seller-applications")]
[TypeFilter(typeof(ApiExceptionFilter))]
[Produces("application/json")]
public class SellerApplicationsController(SellerApplicationService applications) : ControllerBase
{
    /// <summary>Send a request for a seller account; the admins get an email. 409 when one is already open or the account can sell.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimits.SellerApplications)]
    [ProducesResponseType(typeof(MySellerApplicationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit(SellerApplicationRequest req, CancellationToken ct)
    {
        // Optional sign-in: a valid token links the request to that account; no token is fine.
        var auth = await HttpContext.AuthenticateAsync(LocalApi.AuthenticationScheme);
        var userId = auth.Succeeded ? auth.Principal?.FindFirst("sub")?.Value : null;
        return StatusCode(StatusCodes.Status201Created, await applications.SubmitAsync(req, userId, ct));
    }

    /// <summary>The signed-in user's latest request (204 when there is none).</summary>
    [HttpGet("mine")]
    [Authorize(LocalApi.PolicyName)]
    [ProducesResponseType(typeof(MySellerApplicationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        var sub = User.FindFirst("sub")?.Value;
        if (sub is null) return Unauthorized();
        var mine = await applications.MineAsync(sub, ct);
        return mine is null ? NoContent() : Ok(mine);
    }
}

/// <summary>Admin: review seller applications. The seller itself is created with POST /api/admin/sellers (or make-seller).</summary>
[ApiController]
[Route("api/admin/seller-applications")]
[Authorize(LocalApi.PolicyName)]
[AdminOnly]
[TypeFilter(typeof(ApiExceptionFilter))]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
public class AdminSellerApplicationsController(SellerApplicationService applications) : ControllerBase
{
    /// <summary>Newest first. status: New, InReview, Approved, Rejected; search: name, email, business name or city.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<SellerApplicationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<SellerApplicationDto>>> List(
        [FromQuery] string? status, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await applications.ListAsync(status, search, page, pageSize, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SellerApplicationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SellerApplicationDto>> Get(Guid id, CancellationToken ct) => Ok(await applications.GetAsync(id, ct));

    /// <summary>Set InReview / Approved / Rejected with a note (a rejection note is shown to the applicant in the app).</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(SellerApplicationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SellerApplicationDto>> Review(Guid id, ReviewSellerApplicationRequest req, CancellationToken ct) =>
        Ok(await applications.ReviewAsync(id, req, User.FindFirst("name")?.Value ?? User.FindFirst("sub")?.Value, ct));
}
