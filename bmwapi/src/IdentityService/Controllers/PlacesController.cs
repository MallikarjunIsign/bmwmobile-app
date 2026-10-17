using IdentityService.Contracts;
using IdentityService.Infrastructure;
using IdentityService.Services.Places;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static Duende.IdentityServer.IdentityServerConstants;

namespace IdentityService.Controllers;

/// <summary>
/// "Search location" for signed-in users (Gateway: /places/...). The app never calls Google itself: this proxies
/// Google Places (New) and the Geocoding API, and answers from the built-in city list when Google can't be used.
/// </summary>
[ApiController]
[Authorize(LocalApi.PolicyName)]
[Route("api/places")]
[TypeFilter(typeof(ApiExceptionFilter))]
[Produces("application/json")]
public class PlacesController(PlacesService places) : ControllerBase
{
    private string UserId => User.FindFirst("sub")?.Value ?? throw AppException.Unauthorized("Please log in again.", "UNAUTHORIZED");

    /// <summary>
    /// Suggestions while typing (3+ characters). One sessionToken per search, sent again on the details call.
    /// lat/lon bias the results toward the user's location. 429 PLACES_RATE_LIMITED when searching too much.
    /// </summary>
    [HttpGet("autocomplete")]
    [ProducesResponseType(typeof(PlaceAutocompleteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public Task<PlaceAutocompleteResponse> Autocomplete([FromQuery] string? q, [FromQuery] string? sessionToken,
        [FromQuery] double? lat, [FromQuery] double? lon, CancellationToken ct) =>
        places.AutocompleteAsync(UserId, q, sessionToken, lat, lon, ct);

    /// <summary>"Use my current location": the area, city, state, country and postcode of a GPS point.</summary>
    [HttpGet("reverse")]
    [ProducesResponseType(typeof(PlaceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public Task<PlaceDto> Reverse([FromQuery] double? lat, [FromQuery] double? lon, CancellationToken ct) =>
        places.ReverseAsync(UserId, lat, lon, ct);

    /// <summary>
    /// The chosen suggestion: coordinates and address (supported = false outside IN / US / NG / IE).
    /// 503 PLACES_UNAVAILABLE when Google is down – the app then offers the city list.
    /// </summary>
    [HttpGet("{placeId}")]
    [ProducesResponseType(typeof(PlaceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public Task<PlaceDto> Details(string placeId, [FromQuery] string? sessionToken, CancellationToken ct) =>
        places.DetailsAsync(UserId, placeId, sessionToken, ct);
}
