using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuctionService.Entities;

[Table("PartRequests")]
public class PartRequest
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(100)]
    public required string User { get; set; }

    [MaxLength(50)]
    public required string Make { get; set; }

    [MaxLength(50)]
    public required string Model { get; set; }

    public int Year { get; set; }

    [MaxLength(100)]
    public required string PartName { get; set; }

    [MaxLength(20)]
    public string? Vin { get; set; }

    [MaxLength(500)]
    public string? Specs { get; set; }

    public decimal MaxBudget { get; set; }

    [MaxLength(100)]
    public string PreferredPort { get; set; } = "Lagos Apapa";

    [MaxLength(50)]
    public string Status { get; set; } = "Feasibility Checked";

    [MaxLength(500)]
    public string? FeasibilityNote { get; set; }

    public DateTime? EstimatedDeliveryDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
