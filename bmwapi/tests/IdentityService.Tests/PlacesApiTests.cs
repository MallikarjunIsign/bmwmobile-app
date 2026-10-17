using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace IdentityService.Tests;

/// <summary>
/// /api/places through the real service (real tokens and database), Google mocked by the fixture's
/// <see cref="FakeGoogleHandler"/>. Requests Google isn't scripted for get 503 → the built-in city list.
/// </summary>
[Collection("identity-service")]
public class PlacesApiTests(IdentityFixture fx)
{
    private const string Password = "Passw0rd!x";
    // Distinct from the other tests' points, so the reverse cache can't mix them up.
    private const double KukatpallyLat = 17.4948, KukatpallyLon = 78.3996;

    private async Task<string> SignUpAsync(string email)
    {
        using var r = await fx.Client.PostAsJsonAsync("/api/account/register", new
        {
            fullName = "Places Buyer", email, phoneNumber = "+919812345670", password = Password, acceptTerms = true,
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await fx.AppSignInAsync(email, Password)).AccessToken;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"Expected {expected}, got {response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<JsonElement>(text);
    }

    [Fact]
    public async Task Places_NeedASignedInUser()
    {
        using var r = await fx.Client.GetAsync("/api/places/autocomplete?q=Kukatpally");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Search_Pick_AndSave_KeepsThePlaceFields()
    {
        var session = Guid.NewGuid().ToString();
        fx.Google.On(s => FakeGoogleHandler.IsAutocomplete(s) && s.Body!.Contains(session), GoogleSamples.AutocompleteKukatpally)
            .On(s => FakeGoogleHandler.IsDetails(s) && s.Url.Contains(session), GoogleSamples.DetailsKukatpally);
        var token = await SignUpAsync("places.search@test.local");

        var list = await Json(await fx.SendAsync(HttpMethod.Get,
            $"/api/places/autocomplete?q=Kukatpally&sessionToken={session}&lat=17.385&lon=78.4867", token));
        Assert.Equal("google", list.GetProperty("source").GetString());
        var first = list.GetProperty("suggestions")[0];
        Assert.Equal(("Kukatpally", "Hyderabad, Telangana, India"),
            (first.GetProperty("primaryText").GetString(), first.GetProperty("secondaryText").GetString()));

        var place = await Json(await fx.SendAsync(HttpMethod.Get,
            $"/api/places/{first.GetProperty("placeId").GetString()}?sessionToken={session}", token));
        Assert.Equal("Kukatpally, Hyderabad", place.GetProperty("label").GetString());
        Assert.Equal(17.4947934, place.GetProperty("lat").GetDouble(), 6);
        Assert.True(place.GetProperty("supported").GetBoolean());

        // Saved with the new fields (older rows simply have them null).
        var saved = await Json(await fx.SendAsync(HttpMethod.Post, "/api/me/locations", token, new
        {
            label = "Home", placeName = place.GetProperty("label").GetString(),
            latitude = place.GetProperty("lat").GetDouble(), longitude = place.GetProperty("lon").GetDouble(),
            countryCode = "IN", area = "Kukatpally", city = "Hyderabad", state = "Telangana", postalCode = "500072",
            placeId = GoogleSamples.KukatpallyId, formattedAddress = place.GetProperty("formattedAddress").GetString(),
            addressLine = "Flat 402", makePrimary = true,
        }));
        var home = saved.GetProperty("locations").EnumerateArray().Single(l => l.GetProperty("label").GetString() == "Home");
        Assert.Equal(("Kukatpally, Hyderabad", "Hyderabad", "Telangana", GoogleSamples.KukatpallyId),
            (home.GetProperty("placeName").GetString(), home.GetProperty("city").GetString(),
             home.GetProperty("state").GetString(), home.GetProperty("placeId").GetString()));
    }

    [Fact]
    public async Task UseMyCurrentLocation_NamesTheGpsRow_ByAreaAndCity()
    {
        fx.Google.On(s => FakeGoogleHandler.IsReverse(s) && s.Url.Contains("latlng=17.495,78.4"), GoogleSamples.ReverseKukatpally);
        var token = await SignUpAsync("places.gps@test.local");

        var reverse = await Json(await fx.SendAsync(HttpMethod.Get, $"/api/places/reverse?lat={KukatpallyLat}&lon={KukatpallyLon}", token));
        Assert.Equal(("Kukatpally, Hyderabad", "IN", "500072"),
            (reverse.GetProperty("label").GetString(), reverse.GetProperty("countryCode").GetString(), reverse.GetProperty("postcode").GetString()));

        var list = await Json(await fx.SendAsync(HttpMethod.Put, "/api/me/locations/current", token,
            new { latitude = KukatpallyLat, longitude = KukatpallyLon, @explicit = true }));
        var gps = list.GetProperty("locations").EnumerateArray().Single(l => l.GetProperty("isCurrentLocation").GetBoolean());
        Assert.Equal(("Kukatpally, Hyderabad", "Kukatpally", "Hyderabad", "IN"),
            (gps.GetProperty("placeName").GetString(), gps.GetProperty("area").GetString(),
             gps.GetProperty("city").GetString(), gps.GetProperty("countryCode").GetString()));
        Assert.Equal(200, gps.GetProperty("radiusValue").GetInt32());
    }

    [Fact]
    public async Task GoogleDown_FallsBackToTheCityList()
    {
        var token = await SignUpAsync("places.down@test.local");
        // Nothing scripted for "Dubl" → the fake answers 503.
        var list = await Json(await fx.SendAsync(HttpMethod.Get, "/api/places/autocomplete?q=Dubl", token));
        Assert.Equal("cities", list.GetProperty("source").GetString());
        var dublin = Assert.Single(list.GetProperty("suggestions").EnumerateArray());
        Assert.Equal("city:IE:Dublin", dublin.GetProperty("placeId").GetString());

        var place = await Json(await fx.SendAsync(HttpMethod.Get, "/api/places/city:IE:Dublin", token));
        Assert.Equal(("Dublin", "IE"), (place.GetProperty("city").GetString(), place.GetProperty("countryCode").GetString()));
    }

    [Fact]
    public async Task SavingALocationOutsideOurCountries_IsRefused()
    {
        var token = await SignUpAsync("places.uk@test.local");
        var error = await Json(await fx.SendAsync(HttpMethod.Post, "/api/me/locations", token, new
        {
            label = "Home", placeName = "London", latitude = 51.5072, longitude = -0.1276, countryCode = "GB", addressLine = "1 High St",
        }), HttpStatusCode.BadRequest);
        Assert.Equal(("We don't operate in this country yet", "UNSUPPORTED_COUNTRY"),
            (error.GetProperty("message").GetString(), error.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task ShortText_ReturnsNothing_AndBadInput_Is400()
    {
        var token = await SignUpAsync("places.short@test.local");
        var list = await Json(await fx.SendAsync(HttpMethod.Get, "/api/places/autocomplete?q=Ku", token));
        Assert.Empty(list.GetProperty("suggestions").EnumerateArray());

        await Json(await fx.SendAsync(HttpMethod.Get, "/api/places/reverse?lat=95&lon=10", token), HttpStatusCode.BadRequest);
        await Json(await fx.SendAsync(HttpMethod.Get, "/api/places/bad%3Cid%3E", token), HttpStatusCode.BadRequest);
    }
}
