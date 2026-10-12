using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuctionService.Entities;

/// <summary>Custom order statuses: Open until a matching listing is published (Matched) or the buyer closes it.</summary>
public static class CustomOrderStatus
{
    public const string Open = "Open";
    public const string Matched = "Matched";
    public const string Closed = "Closed";
}

/// <summary>
/// A buyer's custom order ("PartRequests" table): the part they searched for but nobody listed yet.
/// When a matching listing is published within the buyer's radius, it becomes Matched and the buyer is notified.
/// </summary>
[Table("PartRequests")]
public class PartRequest
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(100)]
    public required string User { get; set; }

    /// <summary>What the buyer typed in the search box (kept for reference).</summary>
    [MaxLength(200)]
    public string? SearchText { get; set; }

    // Vehicle the part is for. Empty make/model and Year 0 mean "any".
    [MaxLength(50)]
    public required string Make { get; set; }

    [MaxLength(50)]
    public required string Model { get; set; }

    public int Year { get; set; }

    /// <summary>The part type ("Front bumper"); may be empty when only a part number was given.</summary>
    [MaxLength(100)]
    public required string PartName { get; set; }

    [MaxLength(100)]
    public string? PartNumber { get; set; }

    /// <summary>PartNumber uppercased without spaces/dashes, for matching.</summary>
    [MaxLength(100)]
    public string? PartNumberNormalized { get; set; }

    [MaxLength(20)]
    public string? Vin { get; set; }

    [MaxLength(500)]
    public string? Specs { get; set; }

    public decimal MaxBudget { get; set; }

    // Where the buyer is: only listings within RadiusKm match (no radius check when unknown).
    [MaxLength(120)]
    public string? LocationName { get; set; }
    [MaxLength(2)]
    public string? CountryCode { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? RadiusKm { get; set; }

    // Container logistics (hidden in the app for now, kept for the existing data).
    [MaxLength(100)]
    public string PreferredPort { get; set; } = "";

    /// <summary>Open / Matched / Closed (see <see cref="CustomOrderStatus"/>).</summary>
    [MaxLength(50)]
    public string Status { get; set; } = CustomOrderStatus.Open;

    [MaxLength(500)]
    public string? FeasibilityNote { get; set; }

    public DateTime? EstimatedDeliveryDate { get; set; }

    /// <summary>The listing that matched this order.</summary>
    [MaxLength(36)]
    public string? MatchedAuctionId { get; set; }
    public DateTime? MatchedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
