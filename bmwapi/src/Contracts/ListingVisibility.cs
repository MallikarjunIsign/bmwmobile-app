namespace Contracts;

/// <summary>
/// Which listings a buyer sees – the same rule in the Search service (Home, Market, suggestions, seller profile) and
/// the part-request auto-match:
/// <list type="bullet">
/// <item>Pickup only: the item is inside the buyer's radius.</item>
/// <item>Shipping only, or Pickup + Shipping: the item is in the buyer's country, at any distance.</item>
/// <item>Never a listing from another country (shipping stays within the seller's country).</item>
/// </list>
/// </summary>
public static class ListingVisibility
{
    /// <summary>The supported country (IN, US, NG, IE) whose boxes contain the point, or null.</summary>
    public static string? CountryAt(double latitude, double longitude) =>
        RegionRules.SupportedCountries.Order().FirstOrDefault(c => CountryBounds.Contains(c, latitude, longitude));

    /// <summary>"US" from "us", or the country at the coordinates when the code is missing / not supported.</summary>
    public static string? CountryOf(string? countryCode, double? latitude, double? longitude)
    {
        var code = countryCode?.Trim().ToUpperInvariant();
        if (RegionRules.IsSupported(code)) return code;
        return latitude is { } lat && longitude is { } lon ? CountryAt(lat, lon) : null;
    }

    /// <summary>
    /// The rule for one listing. A buyer without coordinates (location off) sees their whole country. A listing that
    /// doesn't ship (pickup only, or older listings without delivery options) needs the radius.
    /// </summary>
    public static bool IsVisible(
        string? buyerCountry, double? buyerLat, double? buyerLon, double radiusKm,
        string? itemCountry, double? itemLat, double? itemLon, bool offersShipping)
    {
        var buyer = CountryOf(buyerCountry, buyerLat, buyerLon);
        var item = CountryOf(itemCountry, itemLat, itemLon);
        if (buyer is not null && item is not null && buyer != item) return false;

        if (offersShipping && buyer is not null && item == buyer) return true;
        if (buyerLat is not { } lat || buyerLon is not { } lon) return buyer is not null && item == buyer;
        if (itemLat is not { } iLat || itemLon is not { } iLon) return false;
        return GeoMath.HaversineKm(lat, lon, iLat, iLon) <= radiusKm;
    }
}
