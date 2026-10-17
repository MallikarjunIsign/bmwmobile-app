using System.Net;
using System.Text.Json;
using IdentityService.Infrastructure;
using IdentityService.Options;
using IdentityService.Services.Places;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IdentityService.Tests;

/// <summary>Log lines kept for the tests (to check the key never reaches the log).</summary>
public class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Lines { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Lines) Lines.Add((logLevel, formatter(state, exception) + exception));
    }
}

/// <summary>
/// "Search location" without the network: Google's answers are mocked. Mapping, session tokens, country rules,
/// limits, cache and the fallback to the built-in city list.
/// </summary>
public class PlacesServiceTests
{
    private const string Key = "test-key-not-real";
    private const string User = "user-1";

    private readonly FakeGoogleHandler _google = new();
    private readonly ListLogger<PlacesUsage> _log = new();
    private PlacesUsage _usage = null!;

    private PlacesService Service(Action<GoogleMapsOptions>? configure = null)
    {
        var opt = new GoogleMapsOptions { ApiKey = Key };
        configure?.Invoke(opt);
        var options = Microsoft.Extensions.Options.Options.Create(opt);
        _usage = new PlacesUsage(options, TimeProvider.System, _log);
        return new PlacesService(new GoogleMapsClient(new HttpClient(_google), options), new PlacesCache(), _usage, options);
    }

    // ── mapping ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Autocomplete_MapsSuggestions_BoldAndGreyText()
    {
        _google.On(FakeGoogleHandler.IsAutocomplete, GoogleSamples.AutocompleteKukatpally);
        var result = await Service().AutocompleteAsync(User, "Kukatpally", null, null, null, default);

        Assert.Equal(PlacesService.SourceGoogle, result.Source);
        Assert.Equal(2, result.Suggestions.Count); // the query prediction (no place ID) is left out
        Assert.Equal(new(GoogleSamples.KukatpallyId, "Kukatpally", "Hyderabad, Telangana, India"), result.Suggestions[0]);
        // No structured format: split the full text at the first comma.
        Assert.Equal(("KPHB Colony", "Kukatpally, Hyderabad, Telangana, India"),
            (result.Suggestions[1].PrimaryText, result.Suggestions[1].SecondaryText));
    }

    [Fact]
    public async Task Details_MapsArea_City_State_Postcode_AndCoordinates()
    {
        _google.On(FakeGoogleHandler.IsDetails, GoogleSamples.DetailsKukatpally);
        var place = await Service().DetailsAsync(User, GoogleSamples.KukatpallyId, null, default);

        Assert.Equal("Kukatpally, Hyderabad", place.Label);
        Assert.Equal(("Kukatpally", "Hyderabad", "Telangana", "TS", "IN", "500072"),
            (place.Area, place.City, place.State, place.StateCode, place.CountryCode, place.Postcode));
        Assert.Equal(17.4947934, place.Lat, 6);
        Assert.Equal(78.3996441, place.Lon, 6);
        Assert.Equal("Kukatpally, Hyderabad, Telangana 500072, India", place.FormattedAddress);
        Assert.True(place.Supported);
        Assert.Null(place.Street);
    }

    [Fact]
    public async Task Details_MapsTheStreet_ForTheAddressForm()
    {
        _google.On(FakeGoogleHandler.IsDetails, GoogleSamples.DetailsMainStreetDublin);
        var place = await Service().DetailsAsync(User, GoogleSamples.DublinId, null, default);

        Assert.Equal(("12 Main Street", "Blanchardstown", "Dublin", "County Dublin", "IE", "D15 X2Y3"),
            (place.Street, place.Area, place.City, place.State, place.CountryCode, place.Postcode));
        Assert.Equal("Blanchardstown, Dublin", place.Label);
    }

    [Fact]
    public async Task Reverse_SkipsPlusCodes_AndMapsHouston()
    {
        _google.On(FakeGoogleHandler.IsReverse, GoogleSamples.ReverseHouston);
        var place = await Service().ReverseAsync(User, 29.7604, -95.3698, default);

        Assert.Equal(("ChIJ-test-houston", "901 Bagby St, Houston, TX 77002, USA"), (place.PlaceId, place.FormattedAddress));
        Assert.Equal(("901 Bagby Street", "Downtown", "Houston", "Texas", "TX", "US", "77002"),
            (place.Street, place.Area, place.City, place.State, place.StateCode, place.CountryCode, place.Postcode));
        Assert.Equal("Downtown, Houston", place.Label);
        // The phone's own point is kept (not Google's building centre).
        Assert.Equal((29.7604, -95.3698), (place.Lat, place.Lon));
        Assert.Equal(PlacesService.SourceGoogle, place.Source);
    }

    [Fact]
    public async Task Reverse_UsesTheSublocality_AsTheArea()
    {
        _google.On(FakeGoogleHandler.IsReverse, GoogleSamples.ReverseKukatpally);
        var place = await Service().ReverseAsync(User, 17.4948, 78.3996, default);
        Assert.Equal("Kukatpally, Hyderabad", place.Label);
        Assert.Equal(("Telangana", "IN", "500072"), (place.State, place.CountryCode, place.Postcode));
    }

    // ── what we send to Google ─────────────────────────────────────────────

    [Fact]
    public async Task SessionToken_IsSentOnAutocomplete_AndOnTheClosingDetailsCall()
    {
        _google.On(FakeGoogleHandler.IsAutocomplete, GoogleSamples.AutocompleteKukatpally)
            .On(FakeGoogleHandler.IsDetails, GoogleSamples.DetailsKukatpally);
        var service = Service();
        var token = Guid.NewGuid().ToString();

        await service.AutocompleteAsync(User, "Kuka", token, 17.385, 78.4867, default);
        await service.AutocompleteAsync(User, "Kukat", token, 17.385, 78.4867, default);
        await service.DetailsAsync(User, GoogleSamples.KukatpallyId, token, default);

        var autocompletes = _google.Requests.Where(FakeGoogleHandler.IsAutocomplete).ToList();
        Assert.Equal(2, autocompletes.Count);
        Assert.All(autocompletes, r => Assert.Equal(token, Body(r).GetProperty("sessionToken").GetString()));
        var details = Assert.Single(_google.Requests, FakeGoogleHandler.IsDetails);
        Assert.Contains($"sessionToken={token}", details.Url);
    }

    [Fact]
    public async Task Autocomplete_IsRestrictedToOurCountries_InEnglish_AndBiasedToTheUser()
    {
        _google.On(FakeGoogleHandler.IsAutocomplete, GoogleSamples.AutocompleteKukatpally);
        await Service().AutocompleteAsync(User, "Main Street", null, 53.35, -6.26, default);

        var sent = Assert.Single(_google.Requests);
        var body = Body(sent);
        Assert.Equal(["in", "us", "ng", "ie"], body.GetProperty("includedRegionCodes").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("en", body.GetProperty("languageCode").GetString());
        var circle = body.GetProperty("locationBias").GetProperty("circle");
        Assert.Equal(53.35, circle.GetProperty("center").GetProperty("latitude").GetDouble());
        Assert.Equal(50_000, circle.GetProperty("radius").GetDouble());
        Assert.False(body.TryGetProperty("sessionToken", out _));
        // The key goes in a header, never in the URL.
        Assert.Equal(Key, sent.Headers["X-Goog-Api-Key"]);
        Assert.DoesNotContain(Key, sent.Url);
    }

    [Fact]
    public async Task Details_AsksOnlyForTheFieldsWeUse()
    {
        _google.On(FakeGoogleHandler.IsDetails, GoogleSamples.DetailsKukatpally);
        await Service().DetailsAsync(User, GoogleSamples.KukatpallyId, null, default);
        var sent = Assert.Single(_google.Requests);
        Assert.Equal("id,formattedAddress,location,addressComponents,displayName", sent.Headers["X-Goog-FieldMask"]);
        Assert.Contains("languageCode=en", sent.Url);
    }

    [Fact]
    public async Task ShortText_AndBadSessionTokens_NeverReachGoogle()
    {
        var service = Service();
        Assert.Empty((await service.AutocompleteAsync(User, "  ab ", null, null, null, default)).Suggestions);
        Assert.Empty(_google.Requests);

        _google.On(FakeGoogleHandler.IsAutocomplete, GoogleSamples.AutocompleteKukatpally);
        await service.AutocompleteAsync(User, "Kukatpally", "not a token!", null, null, default);
        Assert.False(Body(Assert.Single(_google.Requests)).TryGetProperty("sessionToken", out _));
    }

    // ── country restriction ────────────────────────────────────────────────

    [Fact]
    public async Task APlaceOutsideOurCountries_IsMarkedUnsupported()
    {
        _google.On(FakeGoogleHandler.IsDetails, GoogleSamples.DetailsLondon);
        var place = await Service().DetailsAsync(User, GoogleSamples.LondonId, null, default);
        Assert.False(place.Supported);
        Assert.Equal("We don't operate in this country yet", place.Message);
        Assert.Equal("GB", place.CountryCode);
    }

    // ── limits and cache ───────────────────────────────────────────────────

    [Fact]
    public async Task Autocomplete_PerUserLimit_GivesAFriendlyMessage()
    {
        _google.On(FakeGoogleHandler.IsAutocomplete, GoogleSamples.AutocompleteKukatpally);
        var service = Service(o => o.AutocompletePerMinute = 3);
        for (var i = 0; i < 3; i++) await service.AutocompleteAsync(User, "Kukatpally", null, null, null, default);

        var e = await Assert.ThrowsAsync<AppException>(() => service.AutocompleteAsync(User, "Kukatpally", null, null, null, default));
        Assert.Equal((429, "PLACES_RATE_LIMITED"), (e.StatusCode, e.Code));
        Assert.Contains("wait a moment", e.Message);
        // Another user is not affected.
        await service.AutocompleteAsync("user-2", "Kukatpally", null, null, null, default);
        Assert.Equal(4, _google.Count(FakeGoogleHandler.IsAutocomplete));
    }

    [Fact]
    public async Task Autocomplete_PerDayLimit()
    {
        _google.On(FakeGoogleHandler.IsAutocomplete, GoogleSamples.AutocompleteKukatpally);
        var service = Service(o => o.AutocompletePerDay = 2);
        await service.AutocompleteAsync(User, "Kukatpally", null, null, null, default);
        await service.AutocompleteAsync(User, "Kukatpally", null, null, null, default);
        var e = await Assert.ThrowsAsync<AppException>(() => service.AutocompleteAsync(User, "Kukatpally", null, null, null, default));
        Assert.Contains("tomorrow", e.Message);
    }

    [Fact]
    public async Task Details_AreCached_ByPlaceId()
    {
        _google.On(FakeGoogleHandler.IsDetails, GoogleSamples.DetailsKukatpally);
        var service = Service();
        var first = await service.DetailsAsync(User, GoogleSamples.KukatpallyId, "session-a", default);
        var second = await service.DetailsAsync("user-2", GoogleSamples.KukatpallyId, "session-b", default);

        Assert.Equal(1, _google.Count(FakeGoogleHandler.IsDetails));
        Assert.Equal((PlacesService.SourceGoogle, PlacesService.SourceCache), (first.Source, second.Source));
        Assert.Equal(first with { Source = "" }, second with { Source = "" });
        Assert.Equal(1, _usage.Today().CacheHits);
    }

    [Fact]
    public async Task Reverse_IsCached_ByRoundedPoint()
    {
        _google.On(FakeGoogleHandler.IsReverse, GoogleSamples.ReverseHouston);
        var service = Service();
        await service.ReverseAsync(User, 29.76041, -95.36981, default);
        var again = await service.ReverseAsync(User, 29.76039, -95.36979, default); // ~3 m away
        await service.ReverseAsync(User, 29.7704, -95.3698, default);               // ~1 km away: new call

        Assert.Equal(2, _google.Count(FakeGoogleHandler.IsReverse));
        Assert.Equal(PlacesService.SourceCache, again.Source);
        Assert.Equal((29.76039, -95.36979), (again.Lat, again.Lon));
    }

    [Fact]
    public async Task DailyCallLimit_SwitchesToTheCityList()
    {
        _google.On(FakeGoogleHandler.IsAutocomplete, GoogleSamples.AutocompleteKukatpally);
        var service = Service(o => o.DailyCallLimit = 1);
        Assert.Equal(PlacesService.SourceGoogle, (await service.AutocompleteAsync(User, "Hyderabad", null, null, null, default)).Source);
        var second = await service.AutocompleteAsync(User, "Hyderabad", null, null, null, default);

        Assert.Equal(PlacesService.SourceCities, second.Source);
        Assert.Single(_google.Requests);
        Assert.Contains(_log.Lines, l => l.Level == LogLevel.Warning && l.Message.Contains("daily call limit"));
    }

    // ── fallback to the city list ──────────────────────────────────────────

    [Fact]
    public async Task NoKey_UsesTheCityList_AndNeverCallsGoogle()
    {
        var service = Service(o => o.ApiKey = "");
        var result = await service.AutocompleteAsync(User, "hy", null, null, null, default);
        Assert.Equal(PlacesService.SourceGoogle, result.Source); // under 3 characters: nothing at all

        result = await service.AutocompleteAsync(User, "Hyder", null, null, null, default);
        Assert.Equal(PlacesService.SourceCities, result.Source);
        var hyderabad = Assert.Single(result.Suggestions);
        Assert.Equal(("city:IN:Hyderabad", "Hyderabad", "India"), (hyderabad.PlaceId, hyderabad.PrimaryText, hyderabad.SecondaryText));

        // Picking it gives the city's coordinates without Google.
        var place = await service.DetailsAsync(User, hyderabad.PlaceId, null, default);
        Assert.Equal(("Hyderabad", "IN", 17.385), (place.Label, place.CountryCode, place.Lat));

        var gps = await service.ReverseAsync(User, 29.7604, -95.3698, default);
        Assert.Equal(("Houston", "US", PlacesService.SourceCities), (gps.Label, gps.CountryCode, gps.Source));

        Assert.Empty(_google.Requests);
        Assert.Contains(_log.Lines, l => l.Level == LogLevel.Warning && l.Message.Contains("ApiKey is not set"));
    }

    [Fact]
    public async Task GoogleError_FallsBackToTheCityList_AndLogsAWarningWithoutTheKey()
    {
        _google.On(_ => true, GoogleSamples.PermissionDenied, HttpStatusCode.Forbidden);
        var service = Service();

        var result = await service.AutocompleteAsync(User, "Lagos", null, 6.5, 3.4, default);
        Assert.Equal(PlacesService.SourceCities, result.Source);
        Assert.Equal("city:NG:Lagos", Assert.Single(result.Suggestions).PlaceId);

        var gps = await service.ReverseAsync(User, 6.5244, 3.3792, default);
        Assert.Equal(("Lagos", "NG"), (gps.Label, gps.CountryCode));

        // A Google place can't be looked up without Google: 503, the app offers the city list.
        var e = await Assert.ThrowsAsync<AppException>(() => service.DetailsAsync(User, GoogleSamples.KukatpallyId, null, default));
        Assert.Equal((503, "PLACES_UNAVAILABLE"), (e.StatusCode, e.Code));

        Assert.Contains(_log.Lines, l => l.Level == LogLevel.Warning && l.Message.Contains("PERMISSION_DENIED"));
        Assert.DoesNotContain(_log.Lines, l => l.Message.Contains(Key));
    }

    [Fact]
    public async Task UsageLog_CountsCallsPerDay_WithoutKeyOrPersonalData()
    {
        _google.On(FakeGoogleHandler.IsAutocomplete, GoogleSamples.AutocompleteKukatpally)
            .On(FakeGoogleHandler.IsReverse, GoogleSamples.ReverseHouston);
        var service = Service();
        await service.AutocompleteAsync(User, "Kukatpally", null, 17.385, 78.4867, default);
        await service.ReverseAsync(User, 29.7604, -95.3698, default);

        Assert.Equal((1, 0, 1, 0), _usage.Today());
        var line = _log.Lines.Last(l => l.Message.StartsWith("Google Maps calls")).Message;
        Assert.Contains("autocomplete 1, details 0, reverse 1, total 2/5000", line);
        Assert.All(_log.Lines, l =>
        {
            Assert.DoesNotContain(Key, l.Message);
            Assert.DoesNotContain("Kukatpally", l.Message);
            Assert.DoesNotContain(User, l.Message);
            Assert.DoesNotContain("29.76", l.Message);
        });
    }

    [Fact]
    public async Task UnknownPlace_Is404()
    {
        _google.On(FakeGoogleHandler.IsDetails, """{"error":{"code":404,"status":"NOT_FOUND"}}""", HttpStatusCode.NotFound);
        var e = await Assert.ThrowsAsync<AppException>(() => Service().DetailsAsync(User, "ChIJ-gone", null, default));
        Assert.Equal((404, "PLACE_NOT_FOUND"), (e.StatusCode, e.Code));
    }

    private static JsonElement Body(FakeGoogleHandler.Sent sent) => JsonSerializer.Deserialize<JsonElement>(sent.Body!);
}
