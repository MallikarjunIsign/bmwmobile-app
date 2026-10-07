using System.Globalization;
using System.Security.Claims;
using AuctionService.Entities;

namespace AuctionService.RequestHelpers;

/// <summary>
/// The seller's primary location, which IdentityService puts in the access token
/// (claims location_name / location_lat / location_lon). Used as the default item location.
/// </summary>
public static class SellerLocation
{
    public const string NameClaim = "location_name";
    public const string LatitudeClaim = "location_lat";
    public const string LongitudeClaim = "location_lon";

    /// <summary>Fills the item's location from the token when the request didn't send one.</summary>
    public static void ApplyDefault(Item item, ClaimsPrincipal user)
    {
        if (item.Latitude is not null || item.Longitude is not null) return;

        if (double.TryParse(user.FindFirst(LatitudeClaim)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
            double.TryParse(user.FindFirst(LongitudeClaim)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
        {
            item.Latitude = lat;
            item.Longitude = lon;
            item.LocationName ??= user.FindFirst(NameClaim)?.Value;
        }
    }
}
