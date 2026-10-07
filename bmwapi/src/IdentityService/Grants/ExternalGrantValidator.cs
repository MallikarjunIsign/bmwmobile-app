using Duende.IdentityServer.Validation;
using IdentityService.Infrastructure;
using IdentityService.Services;

namespace IdentityService.Grants;

/// <summary>
/// Google / Apple sign-in: grant_type=bmw_external with provider (google|apple), id_token,
/// nonce (Apple), full_name (optional, Apple sends it only once) and role (only when roles are on).
/// No email code: Google / Apple have already verified the person.
/// </summary>
public class ExternalGrantValidator(
    ISocialTokenValidator social,
    AccountService accounts) : IExtensionGrantValidator
{
    public string GrantType => GrantResults.GrantExternal;

    public async Task ValidateAsync(ExtensionGrantValidationContext context)
    {
        try
        {
            var provider = GrantResults.Param(context, "provider")?.ToLowerInvariant();
            var idToken = GrantResults.Param(context, "id_token");
            if (provider is not ("google" or "apple") || idToken is null)
                throw AppException.BadRequest("Sign-in failed. Please try again.", "VALIDATION_FAILED");

            var identity = await social.ValidateAsync(provider, idToken, GrantResults.Param(context, "nonce"));
            var user = await accounts.FindOrCreateExternalUserAsync(
                identity,
                GrantResults.Param(context, "full_name"),
                GrantResults.Param(context, "role"),
                CancellationToken.None);

            context.Result = await GrantResults.SuccessAsync(accounts, user, provider, emailVerified: identity.EmailVerified);
        }
        catch (AppException ex)
        {
            context.Result = GrantResults.Error(ex);
        }
    }
}
