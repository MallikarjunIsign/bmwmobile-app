using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuctionService.Entities;

/// <summary>
/// Shipping price for one size class in one country: BaseFee + distance × RatePerUnit, rounded to a whole number and
/// kept between MinFee and MaxFee. Distance is in Unit (km, or mi in the USA); above MaxDistance shipping isn't offered.
/// Seeded with PLACEHOLDER values (IsPlaceholder) – an admin sets the real ones (PUT /api/shipping/rates/...).
/// </summary>
[Table("ShippingRates")]
public class ShippingRate
{
    public int Id { get; set; }
    /// <summary>IN, US, NG or IE.</summary>
    [MaxLength(2)]
    public required string CountryCode { get; set; }
    public SizeClass SizeClass { get; set; }
    /// <summary>INR, USD, NGN or EUR.</summary>
    [MaxLength(3)]
    public required string Currency { get; set; }
    /// <summary>"km" or "mi".</summary>
    [MaxLength(2)]
    public required string Unit { get; set; }
    [Column(TypeName = "numeric(12,2)")]
    public decimal BaseFee { get; set; }
    [Column(TypeName = "numeric(12,4)")]
    public decimal RatePerUnit { get; set; }
    [Column(TypeName = "numeric(12,2)")]
    public decimal MinFee { get; set; }
    [Column(TypeName = "numeric(12,2)")]
    public decimal MaxFee { get; set; }
    /// <summary>Furthest distance (in Unit) this size class is shipped.</summary>
    public int MaxDistance { get; set; }
    /// <summary>True while the seeded placeholder values haven't been replaced by an admin.</summary>
    public bool IsPlaceholder { get; set; }
    /// <summary>
    /// Industry averages, not a carrier's price (US freight and vehicle transport until a carrier is chosen): the app
    /// labels the charge "estimated rate". Setting real rates (admin API) clears it.
    /// </summary>
    public bool IsEstimate { get; set; }
    /// <summary>Where the numbers come from (sources of the estimate, or the carrier).</summary>
    [MaxLength(500)]
    public string? Source { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(100)]
    public string? UpdatedBy { get; set; }
}
