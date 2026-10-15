using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace IdentityService.Tests;

/// <summary>
/// "Want to sell?" form → stored seller application → admins emailed → admin reviews it and creates the seller with the
/// existing admin API, which marks the application Approved. Real tokens, real database.
/// </summary>
[Collection("identity-service")]
public class SellerApplicationApiTests(IdentityFixture fx)
{
    private const string Password = "Passw0rd!x";

    private static object Form(string email, string name = "Ravi Kumar", string? businessName = "Ravi Auto Parts",
        string phone = "+919876543210", string city = "Hyderabad", string type = "Shop", string sells = "Parts") => new
    {
        fullName = name, email, phoneNumber = phone, countryCode = "IN", city, businessType = type, sells,
        businessName, businessAddress = "12 MG Road, Ameerpet", taxId = "36ABCDE1234F1Z5", message = "Used bumpers and lights",
    };

    private async Task<HttpResponseMessage> SubmitAsync(object form, string? token = null) =>
        await fx.SendAsync(HttpMethod.Post, "/api/seller-applications", token, form);

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"Expected {expected}, got {response.StatusCode}: {text}");
        return string.IsNullOrEmpty(text) ? default : JsonSerializer.Deserialize<JsonElement>(text);
    }

    private async Task<string> AdminTokenAsync() =>
        (await fx.AppSignInAsync(IdentityFixture.AdminEmail, IdentityFixture.AdminPassword)).AccessToken;

    private async Task<string> BuyerTokenAsync(string email)
    {
        using var r = await fx.Client.PostAsJsonAsync("/api/account/register", new
        {
            fullName = "Sita Buyer", email, phoneNumber = "+919876543211", password = Password, acceptTerms = true,
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await fx.AppSignInAsync(email, Password)).AccessToken;
    }

    private async Task<JsonElement> FindAsync(string adminToken, string email)
    {
        var list = await Json(await fx.SendAsync(HttpMethod.Get, $"/api/admin/seller-applications?search={Uri.EscapeDataString(email)}", adminToken),
            HttpStatusCode.OK);
        return Assert.Single(list.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task SignedOut_FormIsStored_AndEveryAdminIsEmailedWithTheDetails()
    {
        var before = fx.Mail.Sent.Count;
        var created = await Json(await SubmitAsync(Form("new.seller1@test.local")), HttpStatusCode.Created);
        Assert.Equal("New", created.GetProperty("status").GetString());

        var mail = fx.Mail.Sent.Skip(before).Single();
        Assert.Contains(mail.To, r => r.Email == IdentityFixture.AdminEmail);
        Assert.Equal("New seller application: Ravi Kumar (Ravi Auto Parts), Hyderabad IN", mail.Subject);
        foreach (var detail in new[] { "new.seller1@test.local", "+919876543210", "Shop", "Parts", "12 MG Road, Ameerpet", "36ABCDE1234F1Z5", "POST /api/admin/sellers" })
            Assert.Contains(detail, mail.Text);

        var stored = await FindAsync(await AdminTokenAsync(), "new.seller1@test.local");
        Assert.Equal("Ravi Auto Parts", stored.GetProperty("businessName").GetString());
        Assert.Equal(JsonValueKind.Null, stored.GetProperty("userId").ValueKind);
    }

    [Theory]
    [InlineData("phone", "98765")]
    [InlineData("city", "")]
    [InlineData("type", "Factory")]
    [InlineData("sells", "Boats")]
    public async Task InvalidForm_Is400_AndNothingIsSent(string field, string value)
    {
        var before = fx.Mail.Sent.Count;
        var form = field switch
        {
            "phone" => Form("bad1@test.local", phone: value),
            "city" => Form("bad2@test.local", city: value),
            "type" => Form("bad3@test.local", type: value),
            _ => Form("bad4@test.local", sells: value),
        };
        await Json(await SubmitAsync(form), HttpStatusCode.BadRequest);
        Assert.Equal(before, fx.Mail.Sent.Count);
    }

    [Fact]
    public async Task SecondRequestWhileOpen_Is409_AndExistingSellersCantApply()
    {
        await Json(await SubmitAsync(Form("twice@test.local")), HttpStatusCode.Created);
        var again = await Json(await SubmitAsync(Form("Twice@Test.local")), HttpStatusCode.Conflict);
        Assert.Equal("APPLICATION_PENDING", again.GetProperty("code").GetString());

        var seller = await Json(await SubmitAsync(Form(IdentityFixture.SellerEmail)), HttpStatusCode.Conflict);
        Assert.Equal("ALREADY_SELLER", seller.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SignedInBuyer_IsLinked_SeesTheStatus_AndMakeSellerApprovesIt()
    {
        var token = await BuyerTokenAsync("buyer.wants.to.sell@test.local");
        await Json(await fx.SendAsync(HttpMethod.Get, "/api/seller-applications/mine", token), HttpStatusCode.NoContent);

        var before = fx.Mail.Sent.Count;
        await Json(await SubmitAsync(Form("buyer.wants.to.sell@test.local", name: "Sita Buyer"), token), HttpStatusCode.Created);
        Assert.Contains("/make-seller", fx.Mail.Sent.Skip(before).Single().Text);

        var mine = await Json(await fx.SendAsync(HttpMethod.Get, "/api/seller-applications/mine", token), HttpStatusCode.OK);
        Assert.Equal("New", mine.GetProperty("status").GetString());

        var admin = await AdminTokenAsync();
        var application = await FindAsync(admin, "buyer.wants.to.sell@test.local");
        var userId = application.GetProperty("userId").GetString()!;
        Assert.False(string.IsNullOrEmpty(userId));

        // The admin uses the existing API: make-seller → the request is Approved and linked to the seller.
        await Json(await fx.SendAsync(HttpMethod.Post, $"/api/admin/users/{userId}/make-seller", admin), HttpStatusCode.OK);
        var done = await FindAsync(admin, "buyer.wants.to.sell@test.local");
        Assert.Equal("Approved", done.GetProperty("status").GetString());
        Assert.Equal(userId, done.GetProperty("sellerUserId").GetString());
    }

    [Fact]
    public async Task AdminCreatesTheSeller_WithTheExistingApi_ApplicationBecomesApproved()
    {
        await Json(await SubmitAsync(Form("from.form@test.local", name: "Musa Dealer", businessName: "Musa Motors")), HttpStatusCode.Created);
        var admin = await AdminTokenAsync();

        var invite = await Json(await fx.SendAsync(HttpMethod.Post, "/api/admin/sellers", admin, new
        {
            fullName = "Musa Dealer", email = "from.form@test.local", phoneNumber = "+919876543210", countryCode = "IN",
            businessName = "Musa Motors", city = "Hyderabad",
        }), HttpStatusCode.Created);

        var application = await FindAsync(admin, "from.form@test.local");
        Assert.Equal("Approved", application.GetProperty("status").GetString());
        Assert.Equal(invite.GetProperty("seller").GetProperty("id").GetString(), application.GetProperty("sellerUserId").GetString());
    }

    [Fact]
    public async Task AdminReview_RejectWithNote_ApplicantSeesIt_AndCanApplyAgain()
    {
        var token = await BuyerTokenAsync("rejected.buyer@test.local");
        await Json(await SubmitAsync(Form("rejected.buyer@test.local"), token), HttpStatusCode.Created);
        var admin = await AdminTokenAsync();
        var id = (await FindAsync(admin, "rejected.buyer@test.local")).GetProperty("id").GetString();

        var reviewed = await Json(await fx.SendAsync(HttpMethod.Patch, $"/api/admin/seller-applications/{id}", admin,
            new { status = "Rejected", adminNote = "Please add your GST number." }), HttpStatusCode.OK);
        Assert.Equal("Rejected", reviewed.GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(reviewed.GetProperty("reviewedBy").GetString()));

        var mine = await Json(await fx.SendAsync(HttpMethod.Get, "/api/seller-applications/mine", token), HttpStatusCode.OK);
        Assert.Equal("Rejected", mine.GetProperty("status").GetString());
        Assert.Equal("Please add your GST number.", mine.GetProperty("adminNote").GetString());

        // A rejected request isn't open: they can send a new one.
        await Json(await SubmitAsync(Form("rejected.buyer@test.local"), token), HttpStatusCode.Created);
    }

    [Fact]
    public async Task AdminEndpoints_AreAdminOnly()
    {
        var buyer = await BuyerTokenAsync("nosy.buyer@test.local");
        var forbidden = await Json(await fx.SendAsync(HttpMethod.Get, "/api/admin/seller-applications", buyer), HttpStatusCode.Forbidden);
        Assert.Equal("ADMIN_ONLY", forbidden.GetProperty("code").GetString());
        await Json(await fx.SendAsync(HttpMethod.Get, "/api/admin/seller-applications", null), HttpStatusCode.Unauthorized);
        await Json(await fx.SendAsync(HttpMethod.Get, "/api/seller-applications/mine", null), HttpStatusCode.Unauthorized);
    }
}
