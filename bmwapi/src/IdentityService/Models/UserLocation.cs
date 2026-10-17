namespace IdentityService.Models;

/// <summary>
/// A place the user buys / sells around (Home, Work, ...), each with its own search radius.
/// Exactly one is the primary location. The row with <see cref="IsCurrentLocation"/> follows the
/// phone's GPS while "auto-update primary" is on; saved places never change by themselves.
/// </summary>
public class UserLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }

    /// <summary>Home, Work, Other or the user's own text. "Current location" for the GPS row.</summary>
    public required string Label { get; set; }

    /// <summary>City or place name shown to the user, e.g. "Hyderabad" or "Near Lagos".</summary>
    public required string PlaceName { get; set; }

    /// <summary>House / flat, building and street (saved addresses only; optional).</summary>
    public string? AddressLine { get; set; }
    /// <summary>Area / locality, e.g. "Kukatpally".</summary>
    public string? Area { get; set; }
    public string? Landmark { get; set; }
    public string? PostalCode { get; set; }

    /// <summary>From "Search location" (Google): the place ID and full address. Null on older rows and the city list.</summary>
    public string? PlaceId { get; set; }
    public string? FormattedAddress { get; set; }
    /// <summary>City and state / region, e.g. "Hyderabad", "Telangana" (null on older rows).</summary>
    public string? City { get; set; }
    public string? State { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int RadiusKm { get; set; }

    /// <summary>IN / US / NG / IE when known (for the popular-city chips).</summary>
    public string? CountryCode { get; set; }

    public bool IsPrimary { get; set; }
    public bool IsCurrentLocation { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
