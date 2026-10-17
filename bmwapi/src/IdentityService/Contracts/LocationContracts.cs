using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts;

public record CityDto(string Name, string CountryCode, double Latitude, double Longitude);

public record PopularCitiesResponse(string CountryCode, string CountryName, IReadOnlyList<CityDto> Cities);

/// <summary>Name for a GPS point. CountryCode is null when it is far from every known city.</summary>
public record NearestCityResponse(string Name, string? CountryCode, double DistanceKm);

/// <summary>RadiusValue / RadiusUnit: the radius the user chose, in the location's unit (mi in the USA, km elsewhere).</summary>
public record UserLocationDto(
    Guid Id,
    string Label,
    string PlaceName,
    double Latitude,
    double Longitude,
    double RadiusKm,
    string? CountryCode,
    bool IsPrimary,
    bool IsCurrentLocation,
    string? AddressLine = null,
    string? Area = null,
    string? Landmark = null,
    string? PostalCode = null,
    int RadiusValue = 0,
    string RadiusUnit = "km",
    // From "Search location" (null on older rows): Google place ID, full address, city and state.
    string? PlaceId = null,
    string? FormattedAddress = null,
    string? City = null,
    string? State = null);

/// <summary>All of the user's locations (primary first) and the auto-update setting.</summary>
public record MyLocationsResponse(bool AutoUpdatePrimary, IReadOnlyList<UserLocationDto> Locations);

public record SaveLocationRequest(
    [StringLength(40, ErrorMessage = "Label must be at most 40 characters.")] string? Label,
    [Required(ErrorMessage = "Choose a place first."), StringLength(120, MinimumLength = 1)] string PlaceName,
    [Required(ErrorMessage = "Latitude is missing.")] double? Latitude,
    [Required(ErrorMessage = "Longitude is missing.")] double? Longitude,
    // Older apps: the radius in km (pulled into range). Missing (and no RadiusValue) → default 200 km / 200 mi.
    double? RadiusKm = null,
    string? CountryCode = null,
    // Add only: also make it the primary location (turns auto-update off).
    bool MakePrimary = false,
    // Optional address details (house / street, area, landmark, PIN / ZIP code).
    [StringLength(200)] string? AddressLine = null,
    [StringLength(100)] string? Area = null,
    [StringLength(100)] string? Landmark = null,
    [StringLength(12)] string? PostalCode = null,
    // The radius in the location's unit (mi in the USA, km elsewhere), 5–1,000. Wins over RadiusKm.
    int? RadiusValue = null,
    // From "Search location" (GET /api/places/{placeId}); all optional.
    [StringLength(300)] string? PlaceId = null,
    [StringLength(300)] string? FormattedAddress = null,
    [StringLength(100)] string? City = null,
    [StringLength(100)] string? State = null);

/// <summary>Change only the radius of a saved location or the GPS row (Home → location line).</summary>
public record RadiusRequest([Required(ErrorMessage = "Choose a radius.")] int? RadiusValue);

/// <summary>
/// The phone's GPS position. Explicit = the user tapped "Use my current location" (also turns auto-update on).
/// Not explicit = the app's automatic check on open; ignored when auto-update is off or the user moved less than 1 km.
/// </summary>
public record CurrentLocationRequest(
    [Required] double? Latitude,
    [Required] double? Longitude,
    double? RadiusKm = null,
    bool Explicit = false,
    // The radius in the location's unit, 5–1,000 (wins over RadiusKm).
    int? RadiusValue = null);

public record LocationSettingsRequest(bool AutoUpdatePrimary);
