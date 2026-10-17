using System.Globalization;
using System.Text.RegularExpressions;
using Contracts;
using IdentityService.Contracts;
using IdentityService.Infrastructure;
using IdentityService.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace IdentityService.Services.Places;

/// <summary>Place details by place ID and reverse-geocoding answers by rounded point (bounded, in memory).</summary>
public class PlacesCache : IDisposable
{
    public MemoryCache Cache { get; } = new(new MemoryCacheOptions { SizeLimit = 100_000 });

    public void Dispose() => Cache.Dispose();
}

/// <summary>
/// "Search location" behind /api/places: Google suggestions, the chosen place's coordinates and address, and the
/// name of the phone's position. Protects the bill (min. 3 characters, per-user limits, a daily limit, 30-day cache)
/// and falls back to the built-in city list when Google can't be used (no key, error, daily limit).
/// </summary>
public partial class PlacesService(
    GoogleMapsClient google,
    PlacesCache cache,
    PlacesUsage usage,
    IOptions<GoogleMapsOptions> options)
{
    public const int MinQueryLength = 3;
    public const int MaxQueryLength = 100;
    public const string SourceGoogle = "google", SourceCache = "cache", SourceCities = "cities";
    public const string UnsupportedCountryMessage = "We don't operate in this country yet";
    private const string CityIdPrefix = "city:";

    private GoogleMapsOptions Opt => options.Value;

    public async Task<PlaceAutocompleteResponse> AutocompleteAsync(string userId, string? q, string? sessionToken,
        double? lat, double? lon, CancellationToken ct)
    {
        var text = q?.Trim() ?? "";
        // The app waits for 3 characters; shorter text never reaches Google (and isn't counted).
        if (text.Length < MinQueryLength) return new([], SourceGoogle);
        if (text.Length > MaxQueryLength) text = text[..MaxQueryLength];
        var token = CleanSessionToken(sessionToken);
        var bias = ValidPoint(lat, lon);

        usage.CheckUser(userId, "autocomplete", Opt.AutocompletePerMinute, Opt.AutocompletePerDay);

        if (CanCallGoogle(GoogleCall.Autocomplete))
        {
            try
            {
                return new(await google.AutocompleteAsync(text, token, bias?.Lat, bias?.Lon, ct), SourceGoogle);
            }
            catch (GoogleMapsException e)
            {
                usage.Fallback(e.Message);
            }
        }
        return new(CitySuggestions(text, bias), SourceCities);
    }

    /// <summary>The chosen suggestion: coordinates and address. City-list IDs ("city:IN:Hyderabad") never call Google.</summary>
    public async Task<PlaceDto> DetailsAsync(string userId, string placeId, string? sessionToken, CancellationToken ct)
    {
        var id = placeId?.Trim() ?? "";
        if (id.Length is 0 or > 300 || !PlaceIdPattern().IsMatch(id))
            throw AppException.BadRequest("This place can't be found. Please search again.", "INVALID_PLACE");

        if (id.StartsWith(CityIdPrefix, StringComparison.OrdinalIgnoreCase)) return CityPlace(id);

        usage.CheckUser(userId, "lookup", Opt.LookupsPerMinute, Opt.LookupsPerDay);
        var token = CleanSessionToken(sessionToken);

        var key = $"details:{id}";
        if (cache.Cache.TryGetValue(key, out GooglePlace? hit) && hit is not null)
        {
            usage.CacheHit();
            return ToDto(hit, SourceCache);
        }

        if (CanCallGoogle(GoogleCall.Details))
        {
            try
            {
                var place = await google.DetailsAsync(id, token, ct);
                Remember(key, place);
                return ToDto(place, SourceGoogle);
            }
            catch (GoogleMapsException e) when (e.NotFound)
            {
                throw AppException.NotFound("This place can't be found. Please search again.", "PLACE_NOT_FOUND");
            }
            catch (GoogleMapsException e)
            {
                usage.Fallback(e.Message);
            }
        }
        throw Unavailable();
    }

    /// <summary>"Use my current location": area, city, state, country and postcode for the phone's position.</summary>
    public async Task<PlaceDto> ReverseAsync(string userId, double? lat, double? lon, CancellationToken ct)
    {
        var point = ValidPoint(lat, lon)
            ?? throw AppException.BadRequest("We couldn't read your location. Please search for the place instead.", "INVALID_LOCATION");
        usage.CheckUser(userId, "lookup", Opt.LookupsPerMinute, Opt.LookupsPerDay);
        return await NameAsync(point.Lat, point.Lon, ct);
    }

    /// <summary>
    /// The name of a point (also used for the GPS location row, without a per-user limit). Always answers: Google when
    /// possible (cached per point rounded to ~110 m), else the nearest city from the built-in list.
    /// </summary>
    public async Task<PlaceDto> NameAsync(double lat, double lon, CancellationToken ct)
    {
        var d = Math.Clamp(Opt.ReverseRoundDecimals, 1, 6);
        var key = string.Create(CultureInfo.InvariantCulture, $"reverse:{Math.Round(lat, d)},{Math.Round(lon, d)}");
        if (cache.Cache.TryGetValue(key, out GooglePlace? hit) && hit is not null)
        {
            usage.CacheHit();
            return ToDto(hit, SourceCache, lat, lon);
        }

        if (CanCallGoogle(GoogleCall.Reverse))
        {
            try
            {
                var place = await google.ReverseAsync(Math.Round(lat, d), Math.Round(lon, d), ct);
                if (place is not null)
                {
                    Remember(key, place);
                    return ToDto(place, SourceGoogle, lat, lon);
                }
            }
            catch (GoogleMapsException e)
            {
                usage.Fallback(e.Message);
            }
        }
        return NearestCity(lat, lon);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private bool CanCallGoogle(GoogleCall kind)
    {
        if (!google.HasKey)
        {
            usage.Fallback("GoogleMaps:ApiKey is not set");
            return false;
        }
        return usage.TryStartGoogleCall(kind);
    }

    private void Remember(string key, GooglePlace place) =>
        cache.Cache.Set(key, place, new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(Math.Max(Opt.CacheDays, 0.0001)),
        });

    private static AppException Unavailable() => new(StatusCodes.Status503ServiceUnavailable,
        "Location search isn't available right now. Please pick a city from the list.", "PLACES_UNAVAILABLE");

    /// <summary>Google allows URL-safe base64 tokens up to 36 characters (a UUID fits); anything else is ignored.</summary>
    private static string? CleanSessionToken(string? token)
    {
        var t = token?.Trim();
        return !string.IsNullOrEmpty(t) && t.Length <= 36 && SessionTokenPattern().IsMatch(t) ? t : null;
    }

    private static (double Lat, double Lon)? ValidPoint(double? lat, double? lon) =>
        lat is { } la && lon is { } lo && GeoMath.IsValidCoordinate(la, lo) ? (la, lo) : null;

    /// <summary>"Kukatpally, Hyderabad" – area + city, else the city, else the place name or first part of the address.</summary>
    public static string Label(string? area, string? city, string? name, string formattedAddress)
    {
        if (!string.IsNullOrWhiteSpace(area) && !string.IsNullOrWhiteSpace(city)) return $"{area}, {city}";
        return new[] { city, area, name, formattedAddress.Split(',')[0] }.First(s => !string.IsNullOrWhiteSpace(s)) ?? "";
    }

    private static PlaceDto ToDto(GooglePlace p, string source, double? lat = null, double? lon = null)
    {
        var supported = RegionRules.IsSupported(p.CountryCode);
        return new PlaceDto(p.PlaceId, p.FormattedAddress, lat ?? p.Lat, lon ?? p.Lon, Label(p.Area, p.City, p.Name, p.FormattedAddress),
            p.Name, p.Street, p.Area, p.City, p.State, p.StateCode, p.CountryCode, p.Postcode,
            supported, supported ? null : UnsupportedCountryMessage, source);
    }

    // ── built-in city list (fallback) ──────────────────────────────────────

    private static IReadOnlyList<PlaceSuggestionDto> CitySuggestions(string text, (double Lat, double Lon)? near)
    {
        var country = near is { } p ? CityCatalog.Describe(p.Lat, p.Lon).CountryCode : null;
        return CityCatalog.Search(text, country)
            .Select(c => new PlaceSuggestionDto($"{CityIdPrefix}{c.CountryCode}:{c.Name}", c.Name, RegionRules.CountryName(c.CountryCode)))
            .ToList();
    }

    private static PlaceDto CityPlace(string id)
    {
        var parts = id.Split(':', 3);
        var city = parts.Length == 3 && CityCatalog.TryGetCountry(parts[1], out var country)
            ? country.Cities.FirstOrDefault(c => string.Equals(c.Name, parts[2], StringComparison.OrdinalIgnoreCase))
            : null;
        if (city is null) throw AppException.NotFound("This place can't be found. Please search again.", "PLACE_NOT_FOUND");

        return new PlaceDto(id, $"{city.Name}, {RegionRules.CountryName(city.CountryCode)}", city.Latitude, city.Longitude, city.Name,
            city.Name, null, null, city.Name, null, null, city.CountryCode, null, true, null, SourceCities);
    }

    private static PlaceDto NearestCity(double lat, double lon)
    {
        var near = CityCatalog.Describe(lat, lon);
        var address = near.CountryCode is null ? near.Name : $"{near.Name}, {RegionRules.CountryName(near.CountryCode)}";
        // Far from every listed city we can't tell the country, so we don't call it unsupported.
        return new PlaceDto(null, address, lat, lon, near.Name, null, null, null, near.CityName, null, null, near.CountryCode, null,
            true, null, SourceCities);
    }

    [GeneratedRegex(@"^[A-Za-z0-9_\-: .']+$")]
    private static partial Regex PlaceIdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_\-]+$")]
    private static partial Regex SessionTokenPattern();
}
