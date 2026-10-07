using Duende.IdentityServer.Validation;
using IdentityService.Infrastructure;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Identity;

namespace IdentityService.Grants;

/// <summary>
/// Step 1 of the mobile login: grant_type=bmw_password with username (email), password, device_id, device_token.
/// A trusted device gets tokens straight away. Otherwise a 6-digit code is emailed and the response is
/// error "invalid_grant" with code OTP_REQUIRED, challengeId, maskedEmail and resendAfterSeconds.
/// </summary>
public class PasswordOtpGrantValidator(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    ITrustedDeviceService devices,
    AccountService accounts,
    ILogger<PasswordOtpGrantValidator> logger) : IExtensionGrantValidator
{
    public string GrantType => GrantResults.GrantPassword;

    public async Task ValidateAsync(ExtensionGrantValidationContext context)
    {
        try
        {
            context.Result = await ValidateCoreAsync(context);
        }
        catch (AppException ex)
        {
            context.Result = GrantResults.Error(ex);
        }
    }

    private async Task<GrantValidationResult> ValidateCoreAsync(ExtensionGrantValidationContext context)
    {
        var email = GrantResults.Param(context, "username");
        var password = context.Request.Raw.Get("password");
        if (email is null || string.IsNullOrEmpty(password))
            return GrantResults.Error("Enter your email and password.", "VALIDATION_FAILED");

        var user = await users.FindByEmailAsync(AccountService.NormalizeEmail(email));
        if (user is null)
            return GrantResults.Error("Incorrect email or password.", "INVALID_CREDENTIALS");

        if (!await users.HasPasswordAsync(user))
        {
            var logins = await users.GetLoginsAsync(user);
            var provider = logins.FirstOrDefault()?.ProviderDisplayName ?? "Google";
            return GrantResults.Error($"This account uses {provider} sign-in. Please use the {provider} button.", "USE_SOCIAL_LOGIN");
        }

        var check = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (check.IsLockedOut)
            return GrantResults.Error("Too many wrong passwords. Please wait a few minutes and try again.", "LOCKED_OUT");
        if (!check.Succeeded)
            return GrantResults.Error("Incorrect email or password.", "INVALID_CREDENTIALS");

        var deviceId = GrantResults.Param(context, "device_id");
        var deviceToken = GrantResults.Param(context, "device_token");
        if (await devices.IsTrustedAsync(user.Id, deviceId, deviceToken))
        {
            logger.LogInformation("{Username} signed in on a trusted device", user.UserName);
            return await GrantResults.SuccessAsync(accounts, user, "pwd", emailVerified: false);
        }

        var challenge = await accounts.StartOtpLoginAsync(user, CancellationToken.None);
        var extra = new Dictionary<string, object>
        {
            ["challengeId"] = challenge.ChallengeId,
            ["maskedEmail"] = challenge.MaskedEmail,
            ["resendAfterSeconds"] = challenge.ResendAfterSeconds,
        };
        if (challenge.DevOtp is not null) extra["devOtp"] = challenge.DevOtp;
        return GrantResults.Error("Enter the code we emailed you.", "OTP_REQUIRED", extra);
    }
}
