using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IdentityService.Data;
using IdentityService.Data.Migrations;
using IdentityService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Tests;

/// <summary>
/// The radius saved with a location: chosen on Add address / "Use my current location" / Home, in the location's unit
/// (miles in the USA, km elsewhere), 5–1,000, default 200. Real tokens, real database.
/// </summary>
[Collection("identity-service")]
public class LocationRadiusApiTests(IdentityFixture fx)
{
    private const string Password = "Passw0rd!x";
    private const double HoustonLat = 29.7604, HoustonLon = -95.3698;

    private async Task<string> SignUpAsync(string email)
    {
        using var r = await fx.Client.PostAsJsonAsync("/api/account/register", new
        {
            fullName = "Radius Buyer", email, phoneNumber = "+12015550123", password = Password, acceptTerms = true,
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

    private static JsonElement Row(JsonElement list, Func<JsonElement, bool> match) =>
        list.GetProperty("locations").EnumerateArray().Single(match);

    private static object Address(string country, double lat, double lon, int? radiusValue) => new
    {
        label = "Home", placeName = country == "US" ? "Houston" : "Hyderabad", latitude = lat, longitude = lon, countryCode = country,
        addressLine = "12 Main St", makePrimary = true, radiusValue,
    };

    [Fact]
    public async Task CurrentLocation_WithoutAChoice_Is200Miles_InTheUsa()
    {
        var token = await SignUpAsync("radius.gps@test.local");
        var list = await Json(await fx.SendAsync(HttpMethod.Put, "/api/me/locations/current", token,
            new { latitude = HoustonLat, longitude = HoustonLon, @explicit = true }));

        var gps = Row(list, l => l.GetProperty("isCurrentLocation").GetBoolean());
        Assert.Equal(200, gps.GetProperty("radiusValue").GetInt32());
        Assert.Equal("mi", gps.GetProperty("radiusUnit").GetString());
        Assert.Equal(321.87, gps.GetProperty("radiusKm").GetDouble(), 2);
    }

    [Fact]
    public async Task ChosenRadius_IsStoredInTheLocationsUnit_AndCanBeChangedLater()
    {
        var token = await SignUpAsync("radius.choose@test.local");
        // "Use my current location" with 25 mi.
        var list = await Json(await fx.SendAsync(HttpMethod.Put, "/api/me/locations/current", token,
            new { latitude = HoustonLat, longitude = HoustonLon, @explicit = true, radiusValue = 25 }));
        var gps = Row(list, l => l.GetProperty("isCurrentLocation").GetBoolean());
        Assert.Equal(25, gps.GetProperty("radiusValue").GetInt32());

        // Home → location line → 500 mi.
        list = await Json(await fx.SendAsync(HttpMethod.Put, $"/api/me/locations/{gps.GetProperty("id").GetString()}/radius", token,
            new { radiusValue = 500 }));
        Assert.Equal(500, Row(list, l => l.GetProperty("isCurrentLocation").GetBoolean()).GetProperty("radiusValue").GetInt32());

        // A new address in India: km, 200 when not chosen.
        list = await Json(await fx.SendAsync(HttpMethod.Post, "/api/me/locations", token, Address("IN", 17.385, 78.4867, null)));
        var home = Row(list, l => l.GetProperty("label").GetString() == "Home");
        Assert.Equal((200, "km"), (home.GetProperty("radiusValue").GetInt32(), home.GetProperty("radiusUnit").GetString()));

        // Edit the address: a custom 350 km.
        var id = home.GetProperty("id").GetString();
        list = await Json(await fx.SendAsync(HttpMethod.Put, $"/api/me/locations/{id}", token, Address("IN", 17.385, 78.4867, 350)));
        Assert.Equal(350, Row(list, l => l.GetProperty("id").GetString() == id).GetProperty("radiusValue").GetInt32());

        // Editing without a radius keeps it.
        list = await Json(await fx.SendAsync(HttpMethod.Put, $"/api/me/locations/{id}", token, Address("IN", 17.4, 78.5, null)));
        Assert.Equal(350, Row(list, l => l.GetProperty("id").GetString() == id).GetProperty("radiusValue").GetInt32());
    }

    [Theory]
    [InlineData(4)]
    [InlineData(1001)]
    public async Task RadiusOutside5To1000_IsRefused(int radiusValue)
    {
        var token = await SignUpAsync($"radius.bad{radiusValue}@test.local");
        var body = await Json(await fx.SendAsync(HttpMethod.Post, "/api/me/locations", token, Address("US", HoustonLat, HoustonLon, radiusValue)),
            HttpStatusCode.BadRequest);
        Assert.Equal("INVALID_RADIUS", body.GetProperty("code").GetString());
        Assert.Contains("between 5 and 1,000 miles", body.GetProperty("message").GetString());

        var list = await Json(await fx.SendAsync(HttpMethod.Post, "/api/me/locations", token, Address("US", HoustonLat, HoustonLon, 1000)));
        Assert.Equal(1000, Row(list, l => l.GetProperty("label").GetString() == "Home").GetProperty("radiusValue").GetInt32());
    }

    [Fact]
    public async Task Migration_ChangesOnlyTheOld60Defaults()
    {
        using var scope = fx.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.FirstAsync();
        UserLocation Loc(string country, int km) => new()
        {
            UserId = user.Id, Label = $"m-{country}-{km}", PlaceName = "x", Latitude = 1, Longitude = 1, CountryCode = country, RadiusKm = km,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        var rows = new[] { Loc("US", 97), Loc("US", 96), Loc("US", 80), Loc("IN", 60), Loc("NG", 60), Loc("IN", 50), Loc("IE", 25), Loc("US", 60) };
        db.UserLocations.AddRange(rows);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync(LocationRadius200.UsaSql);
        await db.Database.ExecuteSqlRawAsync(LocationRadius200.OtherCountriesSql);

        var after = await db.UserLocations.AsNoTracking().Where(l => l.Label.StartsWith("m-")).ToDictionaryAsync(l => l.Label, l => l.RadiusKm);
        // 60 mi (USA) and 60 km (elsewhere): the old default → 200 mi / 200 km.
        Assert.Equal(322, after["m-US-97"]);
        Assert.Equal(322, after["m-US-96"]);
        Assert.Equal(200, after["m-IN-60"]);
        Assert.Equal(200, after["m-NG-60"]);
        // A user's choice (50 mi = 80 km, 50 km, 25 km) or not the US default: kept.
        Assert.Equal(80, after["m-US-80"]);
        Assert.Equal(50, after["m-IN-50"]);
        Assert.Equal(25, after["m-IE-25"]);
        Assert.Equal(60, after["m-US-60"]);

        db.UserLocations.RemoveRange(db.UserLocations.Where(l => l.Label.StartsWith("m-")));
        await db.SaveChangesAsync();
    }
}
