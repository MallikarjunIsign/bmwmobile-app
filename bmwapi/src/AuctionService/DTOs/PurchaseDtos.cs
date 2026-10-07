namespace AuctionService.DTOs;

public class CheckoutDto
{
    public required string AuctionId { get; set; }
    public string DeliveryOption { get; set; } = "PortPickup"; // PortPickup ($0) or DoorstepDelivery ($45)
    public string? DeliveryAddress { get; set; }
    public string PickupPort { get; set; } = "Lagos Apapa";
    public string PaymentMethod { get; set; } = "BankTransfer"; // BankTransfer, Card
}

public class SubmitRatingDto
{
    public int Rating { get; set; } // 1-5
    public string? Comment { get; set; }
}

public class PurchaseDto
{
    public required string Id { get; set; }
    public required string AuctionId { get; set; }
    public required string Buyer { get; set; }
    public required string ItemTitle { get; set; }
    public decimal WinningBidAmount { get; set; }
    public decimal BuyerFee { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal TotalAmount { get; set; }
    public required string DeliveryOption { get; set; }
    public string? DeliveryAddress { get; set; }
    public required string PickupPort { get; set; }
    public required string PaymentMethod { get; set; }
    public required string PaymentStatus { get; set; }
    public required string QrPassCode { get; set; }
    public required string ShippingStatus { get; set; }
    public string? ContainerNumber { get; set; }
    public string? ImageUrl { get; set; }
    public int? Rating { get; set; }
    public string? RatingComment { get; set; }
    public DateTime CreatedAt { get; set; }
}
