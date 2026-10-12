using AuctionService.Services.Geocoding;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuctionService.Controllers;

/// <summary>City, country and default search radius for the phone's position (the app's automatic location).</summary>
public record ReverseGeocodeDto(
    double Latitude,
    double Longitude,
    string? City,
    string? CountryCode,
    bool Supported,
    int RadiusValue,
    string RadiusUnit,
    double RadiusKm);

[ApiController]
[Authorize]
[Route("api/geo")]
public class GeoController(IReverseGeocoder geocoder, ILogger<GeoController> logger) : ControllerBase
{
    /// <summary>
    /// GET /api/geo/reverse?lat=17.38&amp;lon=78.48 → { city: "Hyderabad", countryCode: "IN", radiusValue: 60, radiusUnit: "km" }.
    /// The radius is 60 miles in the USA and 60 km elsewhere.
    /// </summary>
    [HttpGet("reverse")]
    public async Task<ActionResult<ReverseGeocodeDto>> Reverse([FromQuery] double? lat, [FromQuery] double? lon,
        CancellationToken ct)
    {
        if (lat is null || lon is null || !GeoMath.IsValidCoordinate(lat.Value, lon.Value))
            return BadRequest(new { message = "lat and lon must both be valid coordinates" });

        ReverseGeocodeResult place;
        try
        {
            place = await geocoder.ReverseAsync(lat.Value, lon.Value, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // Provider down or misconfigured: the app keeps its last location (or the phone-number country).
            logger.LogWarning(e, "Reverse geocoding failed");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Location lookup is not available right now" });
        }

        var country = place.CountryCode?.ToUpperInvariant();
        var radius = RegionRules.DefaultSearchRadius(country);
        return new ReverseGeocodeDto(lat.Value, lon.Value, place.City, country, RegionRules.IsSupported(country),
            radius.Value, radius.Unit, radius.Km);
    }
}
