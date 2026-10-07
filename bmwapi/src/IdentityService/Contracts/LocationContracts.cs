using System.ComponentModel.DataAnnotations;

namespace IdentityService.Contracts;

public record CityDto(string Name, string CountryCode, double Latitude, double Longitude);

public record PopularCitiesResponse(string CountryCode, string CountryName, IReadOnlyList<CityDto> Cities);

/// <summary>Name for a GPS point. CountryCode is null when it is far from every known city.</summary>
public record NearestCityResponse(string Name, string? CountryCode, double DistanceKm);

public record UserLocationDto(
    Guid Id,
    string Label,
    string PlaceName,
    double Latitude,
    double Longitude,
    int RadiusKm,
    string? CountryCode,
    bool IsPrimary,
    bool IsCurrentLocation);

/// <summary>All of the user's locations (primary first) and the auto-update setting.</summary>
public record MyLocationsResponse(bool AutoUpdatePrimary, IReadOnlyList<UserLocationDto> Locations);

public record SaveLocationRequest(
    [StringLength(40, ErrorMessage = "Label must be at most 40 characters.")] string? Label,
    [Required(ErrorMessage = "Choose a place first."), StringLength(120, MinimumLength = 1)] string PlaceName,
    [Required(ErrorMessage = "Latitude is missing.")] double? Latitude,
    [Required(ErrorMessage = "Longitude is missing.")] double? Longitude,
    // Missing → default (50 km). Out of range → pulled into 5..200 km.
    int? RadiusKm = null,
    string? CountryCode = null,
    // Add only: also make it the primary location (turns auto-update off).
    bool MakePrimary = false);

/// <summary>
/// The phone's GPS position. Explicit = the user tapped "Use my current location" (also turns auto-update on).
/// Not explicit = the app's automatic check on open; ignored when auto-update is off or the user moved less than 1 km.
/// </summary>
public record CurrentLocationRequest(
    [Required] double? Latitude,
    [Required] double? Longitude,
    int? RadiusKm = null,
    bool Explicit = false);

public record LocationSettingsRequest(bool AutoUpdatePrimary);
