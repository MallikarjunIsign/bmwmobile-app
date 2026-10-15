using System.Text.Json;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Validation;
using IdentityService.Infrastructure;
using IdentityService.Models;
using IdentityService.Services;

namespace IdentityService.Grants;

/// <summary>
/// Shared helpers for the mobile grants. Errors keep the OAuth shape
/// ({ "error": "invalid_grant", "error_description": "..." }) plus a "code" the app can check.
/// </summary>
internal static class GrantResults
{
    public const string GrantPassword = "bmw_password";
    public const string GrantOtp = "bmw_otp";
    public const string GrantExternal = "bmw_external";

    public static GrantValidationResult Error(AppException ex) =>
        Error(ex.Message, ex.Code ?? "INVALID_GRANT");

    public static GrantValidationResult Error(string message, string code, Dictionary<string, object>? extra = null)
    {
        var custom = extra ?? new Dictionary<string, object>();
        custom["code"] = code;
        return new GrantValidationResult(TokenRequestErrors.InvalidGrant, message, custom);
    }

    /// <summary>Signed in: the token response also carries the user (and a device token when the device was just trusted).</summary>
    public static async Task<GrantValidationResult> SuccessAsync(
        AccountService accounts, ApplicationUser user, string authMethod, bool emailVerified, string? deviceToken = null)
    {
        if (user.IsDisabled) return Error(AccountService.DisabledMessage, "ACCOUNT_DISABLED");
        await accounts.MarkSignedInAsync(user, emailVerified);
        var custom = new Dictionary<string, object>
        {
            ["user"] = JsonSerializer.SerializeToElement(await accounts.ToDtoAsync(user), JsonSerializerOptions.Web),
        };
        if (deviceToken is not null) custom["deviceToken"] = deviceToken;
        return new GrantValidationResult(user.Id, authMethod, customResponse: custom);
    }

    public static string? Param(ExtensionGrantValidationContext context, string name) =>
        context.Request.Raw.Get(name)?.Trim() is { Length: > 0 } value ? value : null;
}
