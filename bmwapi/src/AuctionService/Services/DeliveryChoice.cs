using AuctionService.Controllers;
using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Services;

/// <summary>Shipping chosen: where it goes and the charge the backend calculated for it.</summary>
public record ShippingChoice(AddressInfo Address, ShippingQuote Quote);

/// <summary>The outcome of checking a shipping choice: the choice, or why not (field errors / a message and code).</summary>
public record DeliveryCheck(ShippingChoice? Choice, string? Message = null, string? Code = null, Dictionary<string, string[]>? Errors = null);

/// <summary>
/// The buyer's shipping address and the charge for it – the same rules for an order's delivery choice and for Buy now
/// checkout: an address-book entry or a new address (saved when asked), in the seller's country, with a delivery location
/// for the distance; the charge is calculated here (never sent by the app).
/// </summary>
public static class DeliveryChoice
{
    public static async Task<DeliveryCheck> ShippingAsync(AuctionDbContext context, ShippingService shipping, string user,
        ChooseDeliveryDto dto, string? countryCode, Item item, SizeClass? sizeClass, DateTime now)
    {
        AddressInfo address;
        if (dto.AddressId is { Length: > 0 } addressId)
        {
            var saved = await context.ShippingAddresses.FirstOrDefaultAsync(a => a.Id == addressId && a.User == user);
            if (saved is null) return new DeliveryCheck(null, "That address isn't in your address book");
            address = Copy(saved.Address);
        }
        else
        {
            var errors = AddressRules.Validate(dto.Address);
            if (errors.Count > 0) return new DeliveryCheck(null, Errors: errors);
            address = AddressRules.ToInfo(dto.Address!);
            if (dto.SaveAddress) await ShippingAddressesController.AddToBookAsync(context, user, address, now);
        }

        if (countryCode is { } country && !string.Equals(address.CountryCode, country, StringComparison.OrdinalIgnoreCase))
            return new DeliveryCheck(null, $"The seller ships within {RegionRules.CountryName(country)} only");

        // The delivery location (saved location or GPS) gives the distance for the charge.
        if (dto.Latitude is { } lat && dto.Longitude is { } lon && GeoMath.IsValidCoordinate(lat, lon))
            (address.Latitude, address.Longitude) = (lat, lon);
        if (address.Latitude is null || address.Longitude is null)
            return new DeliveryCheck(null, "Choose your delivery location (a saved location or your current location) so we can work out the shipping charge",
                "LOCATION_REQUIRED");

        var quote = await shipping.QuoteAsync(item, address.CountryCode, address.Latitude.Value, address.Longitude.Value, sizeClass);
        if (!quote.Available) return new DeliveryCheck(null, quote.Reason, "SHIPPING_NOT_AVAILABLE");
        return new DeliveryCheck(new ShippingChoice(address, quote));
    }

    public static AddressInfo Copy(AddressInfo a) => new()
    {
        Name = a.Name, Phone = a.Phone, Line1 = a.Line1, Line2 = a.Line2, City = a.City, State = a.State, PlaceId = a.PlaceId,
        Postcode = a.Postcode, CountryCode = a.CountryCode, Latitude = a.Latitude, Longitude = a.Longitude,
    };
}
