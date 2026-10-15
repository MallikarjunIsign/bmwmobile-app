namespace Contracts;

/// <summary>
/// A buyer asked for a part nobody has listed yet (custom order). The sellers near the buyer are told ("A buyer is looking
/// for …", live and by push – see PartRequestAudience); when one of them lists a matching part, CustomOrderMatched tells the buyer.
/// </summary>
public class PartRequestCreated
{
    public required string RequestId { get; set; }
    /// <summary>The buyer's username (they are not notified about their own request).</summary>
    public required string Buyer { get; set; }
    /// <summary>"Front bumper – 2016 Toyota Hilux", "Part no. 52119-0K935".</summary>
    public required string Title { get; set; }
    /// <summary>The buyer's area (city), never the exact location.</summary>
    public string? LocationName { get; set; }
    public string? CountryCode { get; set; }
    /// <summary>
    /// The buyer's location and radius: only to pick which sellers are told (PartRequestAudience) – never shown to them.
    /// Null when the buyer's location is off (then: sellers in the same country).
    /// </summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? RadiusKm { get; set; }
    public DateTime CreatedAt { get; set; }
}
