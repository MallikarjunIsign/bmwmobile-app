using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuctionService.Entities;

[Table("Purchases")]
public class Purchase
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(36)]
    public required string AuctionId { get; set; }

    [MaxLength(100)]
    public required string Buyer { get; set; }

    [MaxLength(200)]
    public required string ItemTitle { get; set; }

    public decimal WinningBidAmount { get; set; }
    public decimal BuyerFee { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal TotalAmount { get; set; }

    [MaxLength(50)]
    public string DeliveryOption { get; set; } = "PortPickup"; // PortPickup, DoorstepDelivery

    [MaxLength(300)]
    public string? DeliveryAddress { get; set; }

    [MaxLength(100)]
    public string PickupPort { get; set; } = "Lagos Apapa";

    [MaxLength(50)]
    public string PaymentMethod { get; set; } = "BankTransfer";

    [MaxLength(50)]
    public string PaymentStatus { get; set; } = "Paid";

    [MaxLength(30)]
    public string QrPassCode { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ShippingStatus { get; set; } = "Order Confirmed"; // Order Confirmed, Container Loaded, In Transit, Arrived at Port, Ready for Pickup, Delivered

    [MaxLength(50)]
    public string? ContainerNumber { get; set; }

    [MaxLength(1000)]
    public string? ImageUrl { get; set; }

    public int? Rating { get; set; }
    [MaxLength(500)]
    public string? RatingComment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
