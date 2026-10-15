using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IdentityService.Data;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Tests;

/// <summary>
/// Buyer / seller / admin accounts end to end with real tokens: app sign-up makes buyers, only an admin creates
/// sellers, the invite code sets the seller's password, the role is in the token (and changes on refresh after
/// make-seller), disabled accounts can't sign in, and the one-time backfill of existing users.
/// </summary>
[Collection("identity-service")]
public class AdminSellerApiTests(IdentityFixture fx)
{
    private const string Password = "Passw0rd!x";

    private async Task<string> AdminTokenAsync() =>
        (await fx.AppSignInAsync(IdentityFixture.AdminEmail, IdentityFixture.AdminPassword)).AccessToken;

    private async Task<JsonElement> RegisterBuyerAsync(string email, string name = "Ravi Buyer")
    {
        using var response = await fx.Client.PostAsJsonAsync("/api/account/register", new
        {
            fullName = name, email, phoneNumber = "+919876543210", password = Password, acceptTerms = true,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"Expected {expected}, got {response.StatusCode}: {text}");
        return string.IsNullOrEmpty(text) ? default : JsonSerializer.Deserialize<JsonElement>(text);
    }

    private static string Role(IdentityFixture.TokenResult token) =>
        IdentityFixture.Claims(token.AccessToken).GetProperty("role").GetString()!;

    [Fact]
    public async Task AppSignUp_IsAlwaysABuyer_AndTheTokenSaysSo()
    {
        var user = await RegisterBuyerAsync("buyer.signup@test.local");
        Assert.Equal("buyer", user.GetProperty("role").GetString());

        var token = await fx.AppSignInAsync("buyer.signup@test.local", Password);
        Assert.Equal("buyer", Role(token));
    }

    [Fact]
    public async Task SeededAdminAndSeller_HaveTheirRoles()
    {
        Assert.Equal("admin", Role(await fx.AppSignInAsync(IdentityFixture.AdminEmail, IdentityFixture.AdminPassword)));
        Assert.Equal("seller", Role(await fx.AppSignInAsync(IdentityFixture.SellerEmail, IdentityFixture.SellerPassword)));
    }

    [Fact]
    public async Task AdminEndpoints_NeedAnAdmin_401WithoutToken_FriendlyForbiddenForOthers()
    {
        using var anonymous = await fx.SendAsync(HttpMethod.Get, "/api/admin/sellers", token: null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await RegisterBuyerAsync("buyer.nosy@test.local");
        var buyer = (await fx.AppSignInAsync("buyer.nosy@test.local", Password)).AccessToken;
        var seller = (await fx.AppSignInAsync(IdentityFixture.SellerEmail, IdentityFixture.SellerPassword)).AccessToken;

        foreach (var token in new[] { buyer, seller })
        {
            var body = await Json(await fx.SendAsync(HttpMethod.Post, "/api/admin/sellers", token, new
            {
                fullName = "Sneaky", email = "sneaky@test.local", phoneNumber = "+919876543210", countryCode = "IN",
            }), HttpStatusCode.Forbidden);
            Assert.Equal("ADMIN_ONLY", body.GetProperty("code").GetString());
            Assert.Equal("Only admins can do this.", body.GetProperty("message").GetString());
        }
    }

    [Fact]
    public async Task Admin_CreatesASeller_WhoSetsTheirPasswordWithTheInviteCode_ThenSignsInAsSeller()
    {
        var admin = await AdminTokenAsync();
        var created = await Json(await fx.SendAsync(HttpMethod.Post, "/api/admin/sellers", admin, new
        {
            fullName = "Priya Motors", email = "Priya.Seller@Test.local", phoneNumber = "+919812345678", countryCode = "in",
            businessName = "Priya Auto Parts", city = "Hyderabad",
        }), HttpStatusCode.Created);

        var seller = created.GetProperty("seller");
        var id = seller.GetProperty("id").GetString()!;
        Assert.Equal("seller", seller.GetProperty("role").GetString());
        Assert.Equal("IN", seller.GetProperty("countryCode").GetString());
        Assert.Equal("Priya Auto Parts", seller.GetProperty("businessName").GetString());
        Assert.False(seller.GetProperty("hasPassword").GetBoolean());

        // No password yet: can't sign in.
        var early = await fx.AppSignInAsync("priya.seller@test.local", "anything");
        Assert.Equal("PASSWORD_NOT_SET", early.Code);

        // Resend the invite (newest code wins), then set the password on the "Got a seller invite?" screen.
        using (var db = fx.Host.Services.CreateScope())
        {
            // Skip the 30 s resend cooldown.
            var ctx = db.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var code in ctx.OtpCodes.Where(o => o.User!.Email == "priya.seller@test.local"))
                code.CreatedAt = code.CreatedAt.AddMinutes(-1);
            await ctx.SaveChangesAsync();
        }
        await Json(await fx.SendAsync(HttpMethod.Post, $"/api/admin/sellers/{id}/invite", admin), HttpStatusCode.OK);
        var inviteCode = fx.Emails.CodeFor("priya.seller@test.local", OtpPurpose.SellerInvite);

        await Json(await fx.SendAsync(HttpMethod.Post, "/api/account/reset-password", null, new
        {
            email = "priya.seller@test.local", otp = inviteCode, newPassword = "Pr1ya!Sells",
        }), HttpStatusCode.OK);

        var token = await fx.AppSignInAsync("priya.seller@test.local", "Pr1ya!Sells");
        Assert.Equal(200, token.Status);
        Assert.Equal("seller", Role(token));

        // The invite can't be sent again once the password is set; the code can't be reused.
        var again = await Json(await fx.SendAsync(HttpMethod.Post, $"/api/admin/sellers/{id}/invite", admin), HttpStatusCode.Conflict);
        Assert.Equal("ALREADY_ACTIVE", again.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Admin_CreateSeller_ExistingEmail_Is409_AndPointsToMakeSeller()
    {
        var admin = await AdminTokenAsync();
        var buyer = await RegisterBuyerAsync("buyer.taken@test.local");

        var body = await Json(await fx.SendAsync(HttpMethod.Post, "/api/admin/sellers", admin, new
        {
            fullName = "Taken", email = "buyer.taken@test.local", phoneNumber = "+919876543210", countryCode = "IN",
        }), HttpStatusCode.Conflict);

        Assert.Equal("EMAIL_TAKEN", body.GetProperty("code").GetString());
        Assert.Contains($"/api/admin/users/{buyer.GetProperty("id").GetString()}/make-seller", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task MakeSeller_UpgradesABuyer_AndTheAppGetsTheNewRoleAtTheNextRefresh()
    {
        var admin = await AdminTokenAsync();
        var buyer = await RegisterBuyerAsync("buyer.upgrade@test.local", "Kiran Upgrade");
        var signedIn = await fx.AppSignInAsync("buyer.upgrade@test.local", Password);
        Assert.Equal("buyer", Role(signedIn));

        var upgraded = await Json(await fx.SendAsync(HttpMethod.Post,
            $"/api/admin/users/{buyer.GetProperty("id").GetString()}/make-seller", admin), HttpStatusCode.OK);
        Assert.Equal("seller", upgraded.GetProperty("role").GetString());

        // No new sign-in: the refresh token alone gives a seller token.
        var refreshed = await fx.RefreshAsync(signedIn.RefreshToken!);
        Assert.Equal(200, refreshed.Status);
        Assert.Equal("seller", Role(refreshed));

        // /me agrees.
        var me = await Json(await fx.SendAsync(HttpMethod.Get, "/api/account/me", refreshed.AccessToken), HttpStatusCode.OK);
        Assert.Equal("seller", me.GetProperty("role").GetString());
    }

    [Fact]
    public async Task ListSellers_SearchesByNameOrEmail_AndListsOnlySellers()
    {
        var admin = await AdminTokenAsync();
        await RegisterBuyerAsync("buyer.notlisted@test.local", "Zubair Notaseller");
        await Json(await fx.SendAsync(HttpMethod.Post, "/api/admin/sellers", admin, new
        {
            fullName = "Zubair Spares", email = "zubair.spares@test.local", phoneNumber = "+2348012345678", countryCode = "NG",
        }), HttpStatusCode.Created);

        var byName = await Json(await fx.SendAsync(HttpMethod.Get, "/api/admin/sellers?search=zubair", admin), HttpStatusCode.OK);
        var emails = byName.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("email").GetString()).ToList();
        Assert.Equal(["zubair.spares@test.local"], emails);

        var byEmail = await Json(await fx.SendAsync(HttpMethod.Get, "/api/admin/sellers?search=SPARES@TEST", admin), HttpStatusCode.OK);
        Assert.Equal(1, byEmail.GetProperty("total").GetInt32());

        // All accounts (to find a buyer's id).
        var users = await Json(await fx.SendAsync(HttpMethod.Get, "/api/admin/users?search=notaseller", admin), HttpStatusCode.OK);
        Assert.Equal("buyer", users.GetProperty("items")[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task DisabledSeller_CannotSignInOrRefresh_UntilEnabledAgain()
    {
        var admin = await AdminTokenAsync();
        var created = await Json(await fx.SendAsync(HttpMethod.Post, "/api/admin/sellers", admin, new
        {
            fullName = "Sean Disabled", email = "sean.disabled@test.local", phoneNumber = "+353871234567", countryCode = "IE",
        }), HttpStatusCode.Created);
        var id = created.GetProperty("seller").GetProperty("id").GetString()!;
        await Json(await fx.SendAsync(HttpMethod.Post, "/api/account/reset-password", null, new
        {
            email = "sean.disabled@test.local", otp = fx.Emails.CodeFor("sean.disabled@test.local", OtpPurpose.SellerInvite), newPassword = Password,
        }), HttpStatusCode.OK);
        var signedIn = await fx.AppSignInAsync("sean.disabled@test.local", Password);
        Assert.Equal(200, signedIn.Status);

        var disabled = await Json(await fx.SendAsync(HttpMethod.Patch, $"/api/admin/sellers/{id}", admin, new { disabled = true, city = "Cork" }),
            HttpStatusCode.OK);
        Assert.True(disabled.GetProperty("disabled").GetBoolean());
        Assert.Equal("Cork", disabled.GetProperty("city").GetString());

        var blocked = await fx.AppSignInAsync("sean.disabled@test.local", Password);
        Assert.Equal("ACCOUNT_DISABLED", blocked.Code);
        Assert.NotEqual(200, (await fx.RefreshAsync(signedIn.RefreshToken!)).Status);

        await Json(await fx.SendAsync(HttpMethod.Patch, $"/api/admin/sellers/{id}", admin, new { disabled = false }), HttpStatusCode.OK);
        Assert.Equal(200, (await fx.AppSignInAsync("sean.disabled@test.local", Password)).Status);
    }

    [Fact]
    public async Task Backfill_OlderAccountsBecomeBuyers_AndThoseWithListingsBecomeSellers()
    {
        using var scope = fx.Host.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Two accounts from before roles existed (no role at all).
        var old1 = new ApplicationUser { UserName = "oldlister", Email = "old.lister@test.local", EmailConfirmed = true };
        var old2 = new ApplicationUser { UserName = "oldshopper", Email = "old.shopper@test.local", EmailConfirmed = true };
        await users.CreateAsync(old1, Password);
        await users.CreateAsync(old2, Password);

        Assert.True(SeedData.GiveMissingBuyerRoles(db, users) >= 2);
        Assert.True(await users.IsInRoleAsync(old2, "buyer"));

        // AuctionService says oldlister has listings (admins are never changed).
        db.DataMigrations.RemoveRange(db.DataMigrations.Where(m => m.Name == RoleBackfillService.MigrationName));
        await db.SaveChangesAsync();
        var upgraded = await RoleBackfillService.UpgradeAsync(scope.ServiceProvider, ["oldlister", "nobody"], CancellationToken.None);

        Assert.Equal(1, upgraded);
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        Assert.Equal("seller", await accounts.GetRoleAsync((await users.FindByNameAsync("oldlister"))!));
        Assert.Equal("buyer", await accounts.GetRoleAsync((await users.FindByNameAsync("oldshopper"))!));
        Assert.True(await db.DataMigrations.AnyAsync(m => m.Name == RoleBackfillService.MigrationName));
    }
}
