namespace Contracts;

/// <summary>Shipping container summary, embedded in auction snapshots and search documents.</summary>
public class ContainerInfo
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
}