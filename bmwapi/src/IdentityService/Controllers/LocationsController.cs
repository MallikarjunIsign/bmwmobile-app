using Contracts;
using IdentityService.Contracts;
using IdentityService.Infrastructure;
using IdentityService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static Duende.IdentityServer.IdentityServerConstants;

namespace IdentityService.Controllers;

/// <summary>City list (India, USA, Nigeria, Ireland) for the location screens. Built in and free — no maps API.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/locations")]
[TypeFilter(typeof(ApiExceptionFilter))]
[Produces("application/json")]
public class LocationsController : ControllerBase
{
    /// <summary>The six cities shown as quick-pick chips for a country (IN default, US, NG, IE).</summary>
    [HttpGet("popular-cities")]
    [ProducesResponseType(typeof(PopularCitiesResponse), StatusCodes.Status200OK)]
    public ActionResult<PopularCitiesResponse> PopularCities([FromQuery] string? country = null)
    {
        if (!CityCatalog.TryGetCountry(country, out var c))
            throw AppException.BadRequest("This country isn't supported yet.", "UNKNOWN_COUNTRY");
        return Ok(new PopularCitiesResponse(c.Code, c.Name, CityCatalog.Popular(c).Select(ToDto).ToList()));
    }

    /// <summary>Up to 10 cities in all four countries (names starting with the text first).</summary>
    [HttpGet("cities")]
    [ProducesResponseType(typeof(IReadOnlyList<CityDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<CityDto>> Cities([FromQuery] string? q, [FromQuery] string? country = null) =>
        Ok(CityCatalog.Search(q, country).Select(ToDto).ToList());

    /// <summary>Name for a GPS point: the city, "Near {city}" (over 40 km) or "Current location" (over 150 km).</summary>
    [HttpGet("nearest-city")]
    [ProducesResponseType(typeof(NearestCityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public ActionResult<NearestCityResponse> NearestCity([FromQuery] double? lat, [FromQuery] double? lon)
    {
        if (lat is null || lon is null || !GeoMath.IsValidCoordinate(lat.Value, lon.Value))
            throw AppException.BadRequest("We couldn't read your location. Please pick a city instead.", "INVALID_LOCATION");
        var place = CityCatalog.Describe(lat.Value, lon.Value);
        return Ok(new NearestCityResponse(place.Name, place.CountryCode, place.DistanceKm));
    }

    private static CityDto ToDto(City c) => new(c.Name, c.CountryCode, c.Latitude, c.Longitude);
}

/// <summary>The signed-in user's locations. Every change returns the full, updated list.</summary>
[ApiController]
[Authorize(LocalApi.PolicyName)]
[Route("api/me/locations")]
[TypeFilter(typeof(ApiExceptionFilter))]
[Produces("application/json")]
public class MyLocationsController(LocationService locations) : ControllerBase
{
    private string UserId => User.FindFirst("sub")?.Value ?? throw AppException.Unauthorized("Please log in again.", "UNAUTHORIZED");

    [HttpGet]
    public Task<MyLocationsResponse> Get(CancellationToken ct) => locations.GetAsync(UserId, ct);

    /// <summary>Save a new place (Home, Work, ...). makePrimary=true also makes it the primary location.</summary>
    [HttpPost]
    public Task<MyLocationsResponse> Add(SaveLocationRequest req, CancellationToken ct) => locations.AddAsync(UserId, req, ct);

    /// <summary>Edit label, place or radius. For the GPS row only the radius can change.</summary>
    [HttpPut("{id:guid}")]
    public Task<MyLocationsResponse> Update(Guid id, SaveLocationRequest req, CancellationToken ct) =>
        locations.UpdateAsync(UserId, id, req, ct);

    /// <summary>Delete a saved place. The primary location can't be deleted.</summary>
    [HttpDelete("{id:guid}")]
    public Task<MyLocationsResponse> Delete(Guid id, CancellationToken ct) => locations.DeleteAsync(UserId, id, ct);

    /// <summary>"Set as primary". Turns auto-update off so GPS doesn't overwrite the choice.</summary>
    [HttpPost("{id:guid}/primary")]
    public Task<MyLocationsResponse> SetPrimary(Guid id, CancellationToken ct) => locations.SetPrimaryAsync(UserId, id, ct);

    /// <summary>Phone GPS → primary location ("Use my current location", or the automatic check when the app opens).</summary>
    [HttpPut("current")]
    public Task<MyLocationsResponse> Current(CurrentLocationRequest req, CancellationToken ct) =>
        locations.UpdateCurrentAsync(UserId, req, ct);

    /// <summary>The "Auto-update primary from my current location" switch.</summary>
    [HttpPut("settings")]
    public Task<MyLocationsResponse> Settings(LocationSettingsRequest req, CancellationToken ct) =>
        locations.SetAutoUpdateAsync(UserId, req.AutoUpdatePrimary, ct);
}
