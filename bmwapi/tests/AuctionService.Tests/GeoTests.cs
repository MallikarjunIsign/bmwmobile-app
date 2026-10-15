using System.Net;
using System.Security.Claims;
using System.Text.Json;
using AuctionService.Controllers;
using AuctionService.Services.Geocoding;
using Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AuctionService.Tests;

[Collection("auction-service")]
public class GeoControllerTests(AppFixture fixture)
{
    private async Task<ReverseGeocodeDto> Reverse(double lat, double lon)
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "alice"));
            s.Get.Url(FormattableString.Invariant($"/api/geo/reverse?lat={lat}&lon={lon}"));
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        return await result.ReadAsJsonAsync<ReverseGeocodeDto>();
    }

    [Fact]
    public async Task Hyderabad_IsIndia_With200Km()
    {
        var place = await Reverse(17.4849, 78.4138);

        Assert.Equal("Hyderabad", place.City);
        Assert.Equal("IN", place.CountryCode);
        Assert.True(place.Supported);
        Assert.Equal(200, place.RadiusValue);
        Assert.Equal("km", place.RadiusUnit);
        Assert.Equal(200, place.RadiusKm);
    }

    [Fact]
    public async Task NewYork_IsUsa_With200Miles()
    {
        var place = await Reverse(40.73, -73.99);

        Assert.Equal("New York", place.City);
        Assert.Equal("US", place.CountryCode);
        Assert.Equal(200, place.RadiusValue);
        Assert.Equal("mi", place.RadiusUnit);
        Assert.Equal(321.87, place.RadiusKm, 2);
    }

    [Fact]
    public async Task FarFromEveryKnownCity_HasNoCityOrCountry()
    {
        var place = await Reverse(-33.86, 151.21); // Sydney

        Assert.Null(place.City);
        Assert.Null(place.CountryCode);
        Assert.False(place.Supported);
        Assert.Equal("km", place.RadiusUnit);
    }

    [Fact]
    public async Task InvalidCoordinates_Return400()
    {
        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "alice"));
            s.Get.Url("/api/geo/reverse?lat=95&lon=10");
            s.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task RequiresSignIn()
    {
        await fixture.Host.Scenario(s =>
        {
            s.RemoveRequestHeader("Authorization");
            s.Get.Url("/api/geo/reverse?lat=17.38&lon=78.48");
            s.StatusCodeShouldBe(HttpStatusCode.Unauthorized);
        });
    }
}

public class GeocoderTests
{
    private class CountingGeocoder : IReverseGeocoder
    {
        public int Calls;
        public Task<ReverseGeocodeResult> ReverseAsync(double latitude, double longitude, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new ReverseGeocodeResult("Lagos", "NG", "Test"));
        }
    }

    [Fact]
    public async Task Cache_ReusesTheAnswerForPointsWithinAboutAKilometre()
    {
        var inner = new CountingGeocoder();
        var cached = new CachedReverseGeocoder(inner, new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new GeocodingOptions()));

        await cached.ReverseAsync(6.52441, 3.37921);
        await cached.ReverseAsync(6.52449, 3.37928);
        await cached.ReverseAsync(6.60, 3.40);

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public void Google_ResponseIsParsedToCityAndCountry()
    {
        const string json = """
        {
          "status": "OK",
          "results": [
            { "address_components": [
                { "long_name": "Banjara Hills", "short_name": "Banjara Hills", "types": ["sublocality"] },
                { "long_name": "Hyderabad", "short_name": "Hyderabad", "types": ["locality", "political"] },
                { "long_name": "India", "short_name": "IN", "types": ["country", "political"] }
            ] }
          ]
        }
        """;

        var result = GoogleReverseGeocoder.Parse(JsonDocument.Parse(json).RootElement);

        Assert.Equal("Hyderabad", result.City);
        Assert.Equal("IN", result.CountryCode);
    }

    [Fact]
    public void Google_WithoutLocality_UsesTheDistrictOrPostalTown()
    {
        const string json = """
        { "status": "OK", "results": [ { "address_components": [
            { "long_name": "County Kerry", "short_name": "Kerry", "types": ["administrative_area_level_2"] },
            { "long_name": "Ireland", "short_name": "IE", "types": ["country"] } ] } ] }
        """;

        var result = GoogleReverseGeocoder.Parse(JsonDocument.Parse(json).RootElement);

        Assert.Equal("County Kerry", result.City);
        Assert.Equal("IE", result.CountryCode);
    }

    [Fact]
    public void Google_ZeroResults_IsEmpty()
    {
        var result = GoogleReverseGeocoder.Parse(JsonDocument.Parse("""{ "status": "ZERO_RESULTS", "results": [] }""").RootElement);

        Assert.Null(result.City);
        Assert.Null(result.CountryCode);
    }

    [Theory]
    [InlineData("US", 200, "mi")]
    [InlineData("IN", 200, "km")]
    [InlineData("NG", 200, "km")]
    [InlineData("IE", 200, "km")]
    public void DefaultRadius_Is200InTheCountrysUnit(string country, int value, string unit)
    {
        var radius = RegionRules.DefaultSearchRadius(country);
        Assert.Equal(value, radius.Value);
        Assert.Equal(unit, radius.Unit);
    }
}
