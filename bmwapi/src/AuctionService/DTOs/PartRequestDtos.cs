namespace AuctionService.DTOs;

/// <summary>
/// A custom order, usually pre-filled from a search that found nothing. At least one of PartName, PartNumber
/// or SearchText is needed; make/model/year are optional ("any vehicle").
/// </summary>
public class CreatePartRequestDto
{
    public string? SearchText { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int Year { get; set; }
    public string? PartName { get; set; }
    public string? PartNumber { get; set; }
    public string? Vin { get; set; }
    public string? Specs { get; set; }
    public decimal MaxBudget { get; set; }

    // The buyer's automatic location: only listings within RadiusKm (default 60) will match.
    public string? LocationName { get; set; }
    public string? CountryCode { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? RadiusKm { get; set; }

    // Container logistics (hidden in the app for now).
    public string? PreferredPort { get; set; }
    public string? TargetContainerCode { get; set; }
}

public class PartRequestDto
{
    public required string Id { get; set; }
    public required string User { get; set; }
    public string? SearchText { get; set; }
    public required string Make { get; set; }
    public required string Model { get; set; }
    public int Year { get; set; }
    public required string PartName { get; set; }
    public string? PartNumber { get; set; }
    public string? Vin { get; set; }
    public string? Specs { get; set; }
    public decimal MaxBudget { get; set; }
    public string? LocationName { get; set; }
    public string? CountryCode { get; set; }
    public double? RadiusKm { get; set; }
    public required string PreferredPort { get; set; }
    /// <summary>Open / Matched / Closed.</summary>
    public required string Status { get; set; }
    public string? MatchedAuctionId { get; set; }
    public DateTime? MatchedAt { get; set; }
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
