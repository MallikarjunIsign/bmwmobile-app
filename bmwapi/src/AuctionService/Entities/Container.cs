using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuctionService.Entities;

[Table("Containers")]
public class Container
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    // Unique, e.g. "MSCU1234567" (4 letters + 7 digits, stored upper case).
    [MaxLength(11)]
    public required string ContainerNumber { get; set; }
    [MaxLength(100)]
    public required string OriginPort { get; set; }
    [MaxLength(100)]
    public required string OriginCountry { get; set; }
    [MaxLength(100)]
    public required string DestinationPort { get; set; }
    [MaxLength(100)]
    public required string DestinationCountry { get; set; }
    public DateTime DepartureDate { get; set; }
    public DateTime EstimatedArrivalDate { get; set; }
    public ShippingStatus ShippingStatus { get; set; }
    [MaxLength(100)]
    public required string CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<Item> Items { get; set; } = [];
}