using IdentityService.Options;
using IdentityService.Services.Places;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace IdentityService.Tests;

/// <summary>Runs only with GOOGLE_MAPS_LIVE=1 (real Google calls cost money); skipped otherwise.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GOOGLE_MAPS_LIVE") != "1")
            Skip = "Live Google Maps check: set GOOGLE_MAPS_LIVE=1 to run (uses about 5 real calls)";
    }
}

/// <summary>
/// A few REAL Google calls with the key from IdentityService user-secrets (GoogleMaps:ApiKey) or GoogleMaps__ApiKey.
///   GOOGLE_MAPS_LIVE=1 dotnet test tests/IdentityService.Tests --filter PlacesLiveCheck --logger "console;verbosity=detailed"
/// The key is never printed.
/// </summary>
public class PlacesLiveCheck(ITestOutputHelper output)
{
    [LiveFact]
    public async Task RealGoogle_Kukatpally_MainStreetDublin_ReverseHouston()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets("identity-svc-okoh-parts")
            .AddEnvironmentVariables()
            .Build();
        var opt = new GoogleMapsOptions();
        config.GetSection(GoogleMapsOptions.SectionName).Bind(opt);
        Assert.False(string.IsNullOrWhiteSpace(opt.ApiKey), "GoogleMaps:ApiKey is not set in user-secrets");

        var options = Microsoft.Extensions.Options.Options.Create(opt);
        var usage = new PlacesUsage(options, TimeProvider.System, new ListLogger<PlacesUsage>());
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var places = new PlacesService(new GoogleMapsClient(http, options), new PlacesCache(), usage, options);
        const string user = "live-check";

        // 1. "Kukatpally" (biased to Hyderabad) → pick the first suggestion → Hyderabad with coordinates.
        var session = Guid.NewGuid().ToString();
        var list = await places.AutocompleteAsync(user, "Kukatpally", session, 17.385, 78.4867, default);
        Assert.Equal(PlacesService.SourceGoogle, list.Source);
        var pick = list.Suggestions[0];
        output.WriteLine($"Kukatpally → {pick.PrimaryText} / {pick.SecondaryText}");
        var kukatpally = await places.DetailsAsync(user, pick.PlaceId, session, default);
        output.WriteLine($"  {kukatpally.Label} | {kukatpally.FormattedAddress} | {kukatpally.Lat:F5},{kukatpally.Lon:F5} | {kukatpally.State} {kukatpally.Postcode} {kukatpally.CountryCode}");
        Assert.Equal(("Hyderabad", "IN"), (kukatpally.City, kukatpally.CountryCode));
        Assert.InRange(kukatpally.Lat, 17.3, 17.6);
        Assert.InRange(kukatpally.Lon, 78.3, 78.5);

        // 2. "Main Street Dublin" → Ireland.
        session = Guid.NewGuid().ToString();
        list = await places.AutocompleteAsync(user, "Main Street Dublin", session, null, null, default);
        pick = list.Suggestions[0];
        output.WriteLine($"Main Street Dublin → {pick.PrimaryText} / {pick.SecondaryText}");
        var dublin = await places.DetailsAsync(user, pick.PlaceId, session, default);
        output.WriteLine($"  {dublin.Label} | {dublin.FormattedAddress} | {dublin.Lat:F5},{dublin.Lon:F5}");
        Assert.Equal("IE", dublin.CountryCode);

        // 3. Reverse geocode of Houston downtown → Houston, TX, US.
        var houston = await places.ReverseAsync(user, 29.7604, -95.3698, default);
        output.WriteLine($"Reverse 29.7604,-95.3698 → {houston.Label} | {houston.FormattedAddress} | {houston.StateCode} {houston.CountryCode}");
        Assert.Equal(("Houston", "TX", "US", PlacesService.SourceGoogle), (houston.City, houston.StateCode, houston.CountryCode, houston.Source));

        var (a, d, r, _) = usage.Today();
        output.WriteLine($"Google calls used: autocomplete {a}, details {d}, reverse {r} – total {a + d + r}");
    }
}
