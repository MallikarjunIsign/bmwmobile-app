using Duende.IdentityServer;
using Duende.IdentityServer.Models;
using IdentityService.Grants;

namespace IdentityService;

public static class Config
{
    public static IEnumerable<IdentityResource> IdentityResources =>
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile()
    ];

    public static IEnumerable<ApiScope> ApiScopes =>
    [
        new ApiScope("auctionApp", "Auction app full access"),
        // The Identity service's own /api/account endpoints (used by the mobile app).
        new ApiScope(IdentityServerConstants.LocalApi.ScopeName, "Account API")
    ];

    public static IEnumerable<Client> Clients(IConfiguration config) =>
    [
        new()
        {
            ClientId = "bruno",
            ClientName = "Bruno Client",
            AllowedScopes = { "openid", "profile", "auctionApp" },
            AllowedGrantTypes = GrantTypes.ResourceOwnerPassword,
            ClientSecrets = {new Secret("NotASecret".Sha256())},
        },
        new()
        {
            ClientId = "nextApp",
            ClientName = "NextApp Client",
            ClientSecrets = {new Secret("NotASecret".Sha256())},
            AllowedGrantTypes = GrantTypes.Code,
            RedirectUris = {config["ClientAppUrl"] + "/api/auth/oauth2/callback/duende" },
            AllowedScopes = { "openid", "profile", "auctionApp" },
            AccessTokenLifetime = 3600 * 24 * 30
        },
        // Native mobile app: its own sign-up / login screens, email code (OTP) and Google / Apple sign-in.
        // A public client (no secret can be kept on a phone). Tokens are normal IdentityServer JWTs,
        // so the gateway and all services trust them exactly like the web app's tokens.
        new()
        {
            ClientId = "mobileApp",
            ClientName = "ShopCarParts mobile app",
            RequireClientSecret = false,
            AllowedGrantTypes = { GrantResults.GrantPassword, GrantResults.GrantOtp, GrantResults.GrantExternal },
            AllowedScopes =
            {
                "openid", "profile", "auctionApp", IdentityServerConstants.LocalApi.ScopeName,
                IdentityServerConstants.StandardScopes.OfflineAccess
            },
            AccessTokenLifetime = 3600,
            // "Remember me": the app keeps the refresh token and stays signed in for up to 30 days of inactivity.
            AllowOfflineAccess = true,
            RefreshTokenUsage = TokenUsage.OneTimeOnly,
            RefreshTokenExpiration = TokenExpiration.Sliding,
            SlidingRefreshTokenLifetime = 3600 * 24 * 30,
            AbsoluteRefreshTokenLifetime = 3600 * 24 * 90,
            UpdateAccessTokenClaimsOnRefresh = true
        }
    ];
}
