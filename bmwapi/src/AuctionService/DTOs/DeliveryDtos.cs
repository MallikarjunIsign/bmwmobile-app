namespace AuctionService.DTOs;

/// <summary>A shipping address as the app sends and shows it.</summary>
public class AddressDto
{
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Line1 { get; set; } = "";
    public string? Line2 { get; set; }
    public string City { get; set; } = "";
    /// <summary>Postcode / PIN / ZIP / Eircode.</summary>
    public string Postcode { get; set; } = "";
    /// <summary>IN, US, NG or IE.</summary>
    public string CountryCode { get; set; } = "";
    /// <summary>The delivery location (saved location or GPS); needed for the shipping charge.</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

/// <summary>An address-book entry.</summary>
public class ShippingAddressDto : AddressDto
{
    public string? Id { get; set; }
    public bool IsDefault { get; set; }
}

/// <summary>
/// The buyer chooses how to receive the order. Shipping needs an address: one from the address book (AddressId) or a
/// new one (Address), which is saved to the address book unless SaveAddress is false.
/// </summary>
public class ChooseDeliveryDto
{
    /// <summary>Pickup or Shipping.</summary>
    public string Method { get; set; } = "";
    public string? AddressId { get; set; }
    public AddressDto? Address { get; set; }
    public bool SaveAddress { get; set; } = true;
    /// <summary>
    /// Shipping: the delivery location (one of the buyer's saved locations or the phone's GPS). Used for the shipping
    /// charge; overrides the address's own coordinates. The app never sends a price.
    /// </summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
