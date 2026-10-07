using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth;
using IdentityService.Infrastructure;
using IdentityService.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Services;

public record SocialIdentity(string Provider, string SubjectId, string? Email, bool EmailVerified, string? Name);

public interface ISocialTokenValidator
{
    /// <param name="nonce">Apple only: the raw nonce the app sent to Apple. Its SHA-256 must match the token.</param>
    Task<SocialIdentity> ValidateAsync(string provider, string idToken, string? nonce, CancellationToken ct = default);
}

/// <summary>
/// Verifies Google / Apple ID tokens on the server, so the app can never fake an identity.
/// Google: signature + audience (our Google client IDs).
/// Apple: signature (Apple's public keys), issuer, audience (our bundle ID), lifetime and nonce.
/// </summary>
public class SocialTokenValidator(IOptions<SocialAuthOptions> options, ILogger<SocialTokenValidator> logger) : ISocialTokenValidator
{
    private const string AppleIssuer = "https://appleid.apple.com";

    private static readonly ConfigurationManager<OpenIdConnectConfiguration> AppleConfig = new(
        $"{AppleIssuer}/.well-known/openid-configuration",
        new OpenIdConnectConfigurationRetriever(),
        new HttpDocumentRetriever { RequireHttps = true });

    private readonly SocialAuthOptions _opt = options.Value;

    public Task<SocialIdentity> ValidateAsync(string provider, string idToken, string? nonce, CancellationToken ct = default) =>
        provider.ToLowerInvariant() switch
        {
            "google" => ValidateGoogleAsync(idToken),
            "apple" => ValidateAppleAsync(idToken, nonce, ct),
            _ => throw AppException.BadRequest("Unsupported sign-in provider.", "VALIDATION_FAILED"),
        };

    private async Task<SocialIdentity> ValidateGoogleAsync(string idToken)
    {
        if (_opt.GoogleClientIds.Length == 0)
            throw new AppException(StatusCodes.Status503ServiceUnavailable, "Google sign-in is not set up on the server yet.", "GOOGLE_NOT_CONFIGURED");

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = _opt.GoogleClientIds,
            });
            return new SocialIdentity("google", payload.Subject, payload.Email?.Trim().ToLowerInvariant(), payload.EmailVerified, payload.Name);
        }
        catch (InvalidJwtException ex)
        {
            logger.LogWarning(ex, "Rejected Google ID token");
            throw AppException.Unauthorized("Google sign-in failed. Please try again.", "SOCIAL_TOKEN_INVALID");
        }
    }

    private async Task<SocialIdentity> ValidateAppleAsync(string idToken, string? nonce, CancellationToken ct)
    {
        if (_opt.AppleAudiences.Length == 0)
            throw new AppException(StatusCodes.Status503ServiceUnavailable, "Apple sign-in is not set up on the server yet.", "APPLE_NOT_CONFIGURED");

        if (string.IsNullOrWhiteSpace(nonce))
            throw AppException.BadRequest("Apple sign-in failed. Please try again.", "SOCIAL_TOKEN_INVALID");

        var config = await AppleConfig.GetConfigurationAsync(ct);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer = AppleIssuer,
            ValidAudiences = _opt.AppleAudiences,
            IssuerSigningKeys = config.SigningKeys,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        });

        if (!result.IsValid)
        {
            logger.LogWarning(result.Exception, "Rejected Apple identity token");
            throw AppException.Unauthorized("Apple sign-in failed. Please try again.", "SOCIAL_TOKEN_INVALID");
        }

        string? Claim(string type) => result.Claims.TryGetValue(type, out var v) ? v?.ToString() : null;

        // The app asks Apple to put SHA-256(nonce) in the token, so a stolen token can't be replayed with another nonce.
        var expectedNonce = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nonce))).ToLowerInvariant();
        if (!string.Equals(Claim("nonce"), expectedNonce, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Apple identity token nonce mismatch");
            throw AppException.Unauthorized("Apple sign-in failed. Please try again.", "SOCIAL_TOKEN_INVALID");
        }

        var sub = Claim("sub") ?? throw AppException.Unauthorized("Apple sign-in failed. Please try again.", "SOCIAL_TOKEN_INVALID");
        var email = Claim("email")?.Trim().ToLowerInvariant();
        var verified = string.Equals(Claim("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        return new SocialIdentity("apple", sub, email, verified, null);
    }
}
