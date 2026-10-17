using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IdentityService.Contracts;
using IdentityService.Options;
using Microsoft.Extensions.Options;

namespace IdentityService.Services.Places;

/// <summary>Google said no (bad key, API not enabled, quota, server error, timeout). Reason never contains the key.</summary>
public class GoogleMapsException(string reason, bool notFound = false) : Exception(reason)
{
    public bool NotFound { get; } = notFound;
}

/// <summary>A place as Google describes it (before the country check). Fields are null when Google doesn't say.</summary>
public record GooglePlace(
    string? PlaceId,
    string FormattedAddress,
    double Lat,
    double Lon,
    string? Name,
    string? Street,
    string? Area,
    string? City,
    string? State,
    string? StateCode,
    string? CountryCode,
    string? Postcode);

/// <summary>
/// The three Google Maps calls behind /api/places: Places API (New) Autocomplete and Place Details (only the
/// fields we use: id, formattedAddress, location, addressComponents, displayName) and Geocoding API reverse.
/// No caching, limits or fallback here – see <see cref="PlacesService"/>. Never logs the key or the URL.
/// </summary>
public class GoogleMapsClient(HttpClient http, IOptions<GoogleMapsOptions> options)
{
    public const string DetailsFieldMask = "id,formattedAddress,location,addressComponents,displayName";

    private GoogleMapsOptions Opt => options.Value;

    public bool HasKey => !string.IsNullOrWhiteSpace(Opt.ApiKey);

    /// <summary>POST places:autocomplete – restricted to our countries, biased to (lat, lon) when given.</summary>
    public async Task<IReadOnlyList<PlaceSuggestionDto>> AutocompleteAsync(string input, string? sessionToken,
        double? lat, double? lon, CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["input"] = input,
            ["includedRegionCodes"] = Opt.RegionCodes,
            ["languageCode"] = Opt.Language,
        };
        if (!string.IsNullOrEmpty(sessionToken)) body["sessionToken"] = sessionToken;
        if (lat is { } la && lon is { } lo)
            body["locationBias"] = new
            {
                circle = new { center = new { latitude = la, longitude = lo }, radius = Math.Min(Opt.BiasRadiusMeters, 50_000) },
            };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Opt.PlacesBaseUrl.TrimEnd('/')}/places:autocomplete")
        {
            Content = JsonContent.Create(body),
        };
        using var json = await SendPlacesAsync(request, ct);
        return ParseAutocomplete(json.RootElement);
    }

    /// <summary>GET places/{id} with the field mask; the session token closes the autocomplete session.</summary>
    public async Task<GooglePlace> DetailsAsync(string placeId, string? sessionToken, CancellationToken ct)
    {
        var url = $"{Opt.PlacesBaseUrl.TrimEnd('/')}/places/{Uri.EscapeDataString(placeId)}?languageCode={Uri.EscapeDataString(Opt.Language)}";
        if (!string.IsNullOrEmpty(sessionToken)) url += $"&sessionToken={Uri.EscapeDataString(sessionToken)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Goog-FieldMask", DetailsFieldMask);
        using var json = await SendPlacesAsync(request, ct);
        return ParseDetails(json.RootElement) ?? throw new GoogleMapsException("Place details without a location");
    }

    /// <summary>Geocoding API reverse lookup. Null when Google knows no address there (ZERO_RESULTS).</summary>
    public async Task<GooglePlace?> ReverseAsync(double lat, double lon, CancellationToken ct)
    {
        var url = string.Create(CultureInfo.InvariantCulture,
            $"{Opt.GeocodingUrl}?latlng={lat},{lon}&language={Uri.EscapeDataString(Opt.Language)}&key={Uri.EscapeDataString(Opt.ApiKey ?? "")}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new GoogleMapsException($"Geocoding HTTP {(int)response.StatusCode}");

        using var json = await ReadJsonAsync(response, ct);
        var status = json.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
        if (status == "ZERO_RESULTS") return null;
        if (status != "OK") throw new GoogleMapsException($"Geocoding status {status ?? "?"}");
        return ParseReverse(json.RootElement);
    }

    // ── HTTP ───────────────────────────────────────────────────────────────

    private async Task<JsonDocument> SendPlacesAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Add("X-Goog-Api-Key", Opt.ApiKey);
        using var response = await SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return await ReadJsonAsync(response, ct);

        // { "error": { "code": 403, "status": "PERMISSION_DENIED", ... } } – keep only the status.
        string? status = null;
        try
        {
            using var error = await ReadJsonAsync(response, ct);
            if (error.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("status", out var st)) status = st.GetString();
        }
        catch (Exception ex) when (ex is JsonException or GoogleMapsException) { }
        throw new GoogleMapsException($"Places HTTP {(int)response.StatusCode} {status}".Trim(),
            notFound: response.StatusCode == HttpStatusCode.NotFound || (response.StatusCode == HttpStatusCode.BadRequest && status == "INVALID_ARGUMENT"));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, ct);
        }
        catch (HttpRequestException e)
        {
            throw new GoogleMapsException($"Network error ({e.HttpRequestError})");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GoogleMapsException("Timeout");
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (JsonException)
        {
            throw new GoogleMapsException("Unreadable response");
        }
    }

    // ── parsing (public for the tests) ─────────────────────────────────────

    public static IReadOnlyList<PlaceSuggestionDto> ParseAutocomplete(JsonElement root)
    {
        if (!root.TryGetProperty("suggestions", out var suggestions) || suggestions.ValueKind != JsonValueKind.Array) return [];

        var list = new List<PlaceSuggestionDto>();
        foreach (var s in suggestions.EnumerateArray())
        {
            // Only place predictions (query predictions are not requested and have no place ID).
            if (!s.TryGetProperty("placePrediction", out var p)) continue;
            var id = Str(p, "placeId");
            if (string.IsNullOrEmpty(id)) continue;

            string? main = null, secondary = null;
            if (p.TryGetProperty("structuredFormat", out var f))
            {
                main = Text(f, "mainText");
                secondary = Text(f, "secondaryText");
            }
            if (string.IsNullOrEmpty(main))
            {
                // Fallback: "Kukatpally, Hyderabad, Telangana, India" → "Kukatpally" / "Hyderabad, Telangana, India".
                var full = Text(p, "text") ?? "";
                var comma = full.IndexOf(',');
                main = comma > 0 ? full[..comma] : full;
                secondary ??= comma > 0 ? full[(comma + 1)..].Trim() : "";
            }
            list.Add(new PlaceSuggestionDto(id, main, secondary ?? ""));
        }
        return list;
    }

    public static GooglePlace? ParseDetails(JsonElement root)
    {
        if (!root.TryGetProperty("location", out var loc) ||
            !loc.TryGetProperty("latitude", out var la) || !loc.TryGetProperty("longitude", out var lo)) return null;

        var parts = new AddressParts();
        if (root.TryGetProperty("addressComponents", out var components) && components.ValueKind == JsonValueKind.Array)
            foreach (var c in components.EnumerateArray())
                parts.Add(Types(c), Str(c, "longText"), Str(c, "shortText"));

        return parts.ToPlace(Str(root, "id"), Str(root, "formattedAddress") ?? "", la.GetDouble(), lo.GetDouble(),
            Text(root, "displayName"));
    }

    /// <summary>
    /// The most exact result (not a plus code) gives the address and place ID; area / city / state / postcode are
    /// taken from the first result that has them.
    /// </summary>
    public static GooglePlace? ParseReverse(JsonElement root)
    {
        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return null;

        JsonElement? best = null;
        var parts = new AddressParts();
        foreach (var r in results.EnumerateArray())
        {
            if (r.TryGetProperty("types", out var t) && t.EnumerateArray().Any(x => x.GetString() == "plus_code")) continue;
            var isBest = best is null;
            best ??= r;
            if (!r.TryGetProperty("address_components", out var components)) continue;
            // The street only from the most exact result (broader results describe a different point).
            foreach (var c in components.EnumerateArray())
                parts.Add(Types(c), Str(c, "long_name"), Str(c, "short_name"), withStreet: isBest);
        }
        if (best is not { } b) return null;

        var geometry = b.GetProperty("geometry").GetProperty("location");
        return parts.ToPlace(Str(b, "place_id"), Str(b, "formatted_address") ?? "", geometry.GetProperty("lat").GetDouble(),
            geometry.GetProperty("lng").GetDouble(), null);
    }

    /// <summary>Collects the address parts we keep from Google's address components (first value wins).</summary>
    private sealed class AddressParts
    {
        private string? _streetNumber, _route, _sublocality1, _sublocality, _neighborhood, _locality, _postalTown,
            _admin3, _admin2, _state, _stateCode, _country, _postcode;

        public void Add(IReadOnlyCollection<string> types, string? longText, string? shortText, bool withStreet = true)
        {
            if (string.IsNullOrWhiteSpace(longText)) return;
            void Set(string type, ref string? field) { if (types.Contains(type)) field ??= longText; }

            if (withStreet)
            {
                Set("street_number", ref _streetNumber);
                Set("route", ref _route);
            }
            Set("sublocality_level_1", ref _sublocality1);
            Set("sublocality", ref _sublocality);
            Set("neighborhood", ref _neighborhood);
            Set("locality", ref _locality);
            Set("postal_town", ref _postalTown);
            Set("administrative_area_level_3", ref _admin3);
            Set("administrative_area_level_2", ref _admin2);
            Set("postal_code", ref _postcode);
            if (types.Contains("administrative_area_level_1"))
            {
                _state ??= longText;
                _stateCode ??= shortText ?? longText;
            }
            if (types.Contains("country")) _country ??= (shortText ?? longText).ToUpperInvariant();
        }

        public GooglePlace ToPlace(string? placeId, string formattedAddress, double lat, double lon, string? name)
        {
            var city = _locality ?? _postalTown ?? _admin3 ?? _admin2;
            var area = _sublocality1 ?? _sublocality ?? _neighborhood;
            if (string.Equals(area, city, StringComparison.OrdinalIgnoreCase)) area = null;
            var street = _route is null ? null : string.Join(' ', new[] { _streetNumber, _route }.Where(p => p is not null));
            return new GooglePlace(placeId, formattedAddress, lat, lon, name, street, area, city, _state, _stateCode, _country, _postcode);
        }
    }

    private static List<string> Types(JsonElement component) =>
        component.TryGetProperty("types", out var t) && t.ValueKind == JsonValueKind.Array
            ? t.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
            : [];

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>{ "mainText": { "text": "Kukatpally" } } → "Kukatpally".</summary>
    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? Str(v, "text") : null;
}
