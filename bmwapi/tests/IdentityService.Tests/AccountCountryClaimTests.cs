namespace IdentityService.Tests;

/// <summary>AuctionService opens a seller's payment account in the account's own country (the account_country claim).</summary>
[Collection("identity-service")]
public class AccountCountryClaimTests(IdentityFixture fx)
{
    [Fact]
    public async Task SellerToken_CarriesTheAccountsCountry()
    {
        var token = await fx.AppSignInAsync(IdentityFixture.SellerEmail, IdentityFixture.SellerPassword);
        var claims = IdentityFixture.Claims(token.AccessToken);

        Assert.True(claims.TryGetProperty("account_country", out var country), "no account_country claim");
        Assert.Matches("^(IN|US|NG|IE)$", country.GetString());
    }
}
