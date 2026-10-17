namespace IdentityService.Contracts;

/// <summary>One row of the "Search location" list: "Kukatpally" (bold) / "Hyderabad, Telangana, India" (grey).</summary>
public record PlaceSuggestionDto(string PlaceId, string PrimaryText, string SecondaryText);

/// <summary>
/// GET /api/places/autocomplete. Source: "google", or "cities" when Google is not available (no key, error or the
/// daily limit is used up) – then the suggestions come from the built-in city list.
/// </summary>
public record PlaceAutocompleteResponse(IReadOnlyList<PlaceSuggestionDto> Suggestions, string Source);

/// <summary>
/// A chosen place (GET /api/places/{placeId}) or the phone's position (GET /api/places/reverse).
/// Label is what the app shows: "Kukatpally, Hyderabad" (area + city), or the city when there is no area.
/// Supported is false outside India, USA, Nigeria and Ireland (Message then says so).
/// </summary>
public record PlaceDto(
    string? PlaceId,
    string FormattedAddress,
    double Lat,
    double Lon,
    string Label,
    string? Name,
    string? Street,
    string? Area,
    string? City,
    string? State,
    string? StateCode,
    string? CountryCode,
    string? Postcode,
    bool Supported,
    string? Message,
    string Source);
