using System.Globalization;
using System.Security.Claims;

namespace Contracts;

/// <summary>
/// Which sellers hear about a buyer's part request – the same rule for the live banner, the push notification and the
/// "Buyer requests" list:
/// <list type="bullet">
/// <item>Never a seller in another country.</item>
/// <item>The buyer gave their location: only sellers within the buyer's radius (60 km / 60 mi by default).</item>
/// <item>No buyer location: sellers in the buyer's country.</item>
/// </list>
/// A seller whose location / country isn't known can't be placed, so they aren't told.
/// </summary>
public static class PartRequestAudience
{
    public const double DefaultRadiusKm = 60;

    // Claims IdentityService puts in the access token from the user's primary location.
    public const string LatitudeClaim = "location_lat";
    public const string LongitudeClaim = "location_lon";
    public const string CountryClaim = "location_country";

    public static bool Reaches(
        string? requestCountry, double? requestLat, double? requestLon, double? requestRadiusKm,
        string? sellerCountry, double? sellerLat, double? sellerLon)
    {
        var buyerCountry = Normalize(requestCountry);
        var country = Normalize(sellerCountry);
        if (buyerCountry is not null && country is not null && buyerCountry != country) return false;

        if (requestLat is { } lat && requestLon is { } lon)
        {
            if (sellerLat is not { } sLat || sellerLon is not { } sLon) return false;
            var radius = requestRadiusKm is > 0 ? requestRadiusKm.Value : DefaultRadiusKm;
            return GeoMath.HaversineKm(lat, lon, sLat, sLon) <= radius;
        }

        return buyerCountry is not null && country == buyerCountry;
    }

    public static bool Reaches(PartRequestCreated request, string? sellerCountry, double? sellerLat, double? sellerLon) =>
        Reaches(request.CountryCode, request.Latitude, request.Longitude, request.RadiusKm, sellerCountry, sellerLat, sellerLon);

    /// <summary>The seller's location from their token (null when they have none).</summary>
    public static (string? Country, double? Latitude, double? Longitude) FromClaims(ClaimsPrincipal? user) =>
        (Normalize(user?.FindFirst(CountryClaim)?.Value), Number(user?.FindFirst(LatitudeClaim)?.Value), Number(user?.FindFirst(LongitudeClaim)?.Value));

    private static string? Normalize(string? country) =>
        string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant();

    private static double? Number(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
}
