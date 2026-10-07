using Duende.IdentityServer.Validation;
using IdentityService.Infrastructure;
using IdentityService.Models;
using IdentityService.Services;

namespace IdentityService.Grants;

/// <summary>
/// Step 2 of the mobile login: grant_type=bmw_otp with challenge_id, otp, device_id and remember_device.
/// With remember_device=true the response also has a "deviceToken" that skips the code for 30 days.
/// </summary>
public class OtpGrantValidator(
    IOtpService otp,
    ITrustedDeviceService devices,
    AccountService accounts) : IExtensionGrantValidator
{
    public string GrantType => GrantResults.GrantOtp;

    public async Task ValidateAsync(ExtensionGrantValidationContext context)
    {
        try
        {
            var user = await accounts.UserFromChallengeAsync(GrantResults.Param(context, "challenge_id"));

            var code = GrantResults.Param(context, "otp");
            if (code is null || code.Length != 6 || !code.All(char.IsDigit))
                throw AppException.BadRequest("Enter the 6-digit code.", "VALIDATION_FAILED");

            await otp.VerifyAsync(user, OtpPurpose.Login, code);

            string? deviceToken = null;
            var deviceId = GrantResults.Param(context, "device_id");
            var remember = string.Equals(GrantResults.Param(context, "remember_device"), "true", StringComparison.OrdinalIgnoreCase);
            if (remember && deviceId is not null && deviceId.Length <= 100)
                deviceToken = await devices.TrustAsync(user.Id, deviceId);

            context.Result = await GrantResults.SuccessAsync(accounts, user, "otp", emailVerified: true, deviceToken);
        }
        catch (AppException ex)
        {
            context.Result = GrantResults.Error(ex);
        }
    }
}
