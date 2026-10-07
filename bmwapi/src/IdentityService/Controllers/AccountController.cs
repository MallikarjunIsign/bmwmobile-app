using IdentityService.Contracts;
using IdentityService.Infrastructure;
using IdentityService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using static Duende.IdentityServer.IdentityServerConstants;

namespace IdentityService.Controllers;

/// <summary>
/// JSON endpoints for the mobile app. Logging in itself goes through the standard token endpoint
/// (/connect/token with grant types bmw_password, bmw_otp, bmw_external and refresh_token).
/// </summary>
[ApiController]
[Route("api/account")]
[EnableRateLimiting(RateLimits.Account)]
[TypeFilter(typeof(ApiExceptionFilter))]
[Produces("application/json")]
public class AccountController(AccountService accounts) : ControllerBase
{
    /// <summary>Create an account. The app then logs in with email + password, which emails the first code.</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRequest req, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await accounts.RegisterAsync(req, ct));

    /// <summary>Email a new login code for the challenge returned by the bmw_password grant.</summary>
    [HttpPost("otp/resend")]
    [ProducesResponseType(typeof(OtpSentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<OtpSentResponse>> ResendOtp(ResendOtpRequest req, CancellationToken ct) =>
        Ok(await accounts.ResendLoginOtpAsync(req.ChallengeId, ct));

    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(OtpSentResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OtpSentResponse>> ForgotPassword(ForgotPasswordRequest req, CancellationToken ct) =>
        Ok(await accounts.ForgotPasswordAsync(req, ct));

    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MessageResponse>> ResetPassword(ResetPasswordRequest req, CancellationToken ct)
    {
        await accounts.ResetPasswordAsync(req, ct);
        return Ok(new MessageResponse("Password updated. Please log in with your new password."));
    }

    /// <summary>The signed-in user (access token with the IdentityServerApi scope).</summary>
    [Authorize(LocalApi.PolicyName)]
    [DisableRateLimiting]
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> Me()
    {
        var sub = User.FindFirst("sub")?.Value;
        if (sub is null) return Unauthorized();
        return Ok(await accounts.GetUserAsync(sub));
    }
}
