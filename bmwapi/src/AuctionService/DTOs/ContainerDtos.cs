using AuctionService.Entities;

namespace AuctionService.DTOs;

public class ContainerDto
{
    public required string Id { get; set; }
    public required string ContainerNumber { get; set; }
    public required string OriginPort { get; set; }
    public required string OriginCountry { get; set; }
    public required string DestinationPort { get; set; }
    public required string DestinationCountry { get; set; }
    public DateTime DepartureDate { get; set; }
    public DateTime EstimatedArrivalDate { get; set; }
    public required string ShippingStatus { get; set; }
    public required string CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int AuctionCount { get; set; }
}

public class ContainerDetailsDto : ContainerDto
{
    public List<AuctionDto> Auctions { get; set; } = [];
}

/// <summary>Body of POST /containers and PUT /containers/{id} (PUT replaces all fields).</summary>
public class SaveContainerDto
{
    public string ContainerNumber { get; set; } = "";
    public string OriginPort { get; set; } = "";
    public string OriginCountry { get; set; } = "";
    public string DestinationPort { get; set; } = "";
    public string DestinationCountry { get; set; } = "";
    public DateTime DepartureDate { get; set; }
    public DateTime EstimatedArrivalDate { get; set; }
    public ShippingStatus ShippingStatus { get; set; } = ShippingStatus.Loading;
}

public class PlannedContainerScheduleDto
{
    public required string Id { get; set; }
    public required string Code { get; set; }
    public required string OriginPort { get; set; }
    public required string OriginCountry { get; set; }
    public required string DestinationPort { get; set; }
    public required string DestinationCountry { get; set; }
    public DateTime CutoffDate { get; set; }
    public int DaysRemaining { get; set; }
    public DateTime DepartureDate { get; set; }
    public DateTime EstimatedArrivalDate { get; set; }
    public int CapacityPercentage { get; set; }
    public required string Status { get; set; } // Receiving Parts, Scheduled, Closed
}