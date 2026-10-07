namespace AuctionService.DTOs;

public class CreatePartRequestDto
{
    public required string Make { get; set; }
    public required string Model { get; set; }
    public int Year { get; set; }
    public required string PartName { get; set; }
    public string? Vin { get; set; }
    public string? Specs { get; set; }
    public decimal MaxBudget { get; set; }
    public string PreferredPort { get; set; } = "Lagos Apapa";
    public string? TargetContainerCode { get; set; }
}

public class PartRequestDto
{
    public required string Id { get; set; }
    public required string User { get; set; }
    public required string Make { get; set; }
    public required string Model { get; set; }
    public int Year { get; set; }
    public required string PartName { get; set; }
    public string? Vin { get; set; }
    public string? Specs { get; set; }
    public decimal MaxBudget { get; set; }
    public required string PreferredPort { get; set; }
    public required string Status { get; set; }
    public string? FeasibilityNote { get; set; }
    public DateTime? EstimatedDeliveryDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class FeasibilityCheckResultDto
{
    public bool IsFeasible { get; set; }
    public required string TargetContainerCode { get; set; }
    public required string FeasibilityStatus { get; set; }
    public DateTime CutoffDate { get; set; }
    public int DaysRemaining { get; set; }
    public required string PickupPort { get; set; }
    public string? Note { get; set; }
}
