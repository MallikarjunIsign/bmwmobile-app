namespace Contracts;

/// <summary>An order moved to a new tracking step ("Shipped", "Ready for pickup", …); tells the other party.</summary>
public class OrderStatusChanged
{
    public required string OrderId { get; set; }
    public required string AuctionId { get; set; }
    public required string Title { get; set; }
    /// <summary>The step's name in the order's pipeline, e.g. "Out for delivery".</summary>
    public required string StatusLabel { get; set; }
    /// <summary>Who gets the notification (the other party of the sale).</summary>
    public required string NotifyUser { get; set; }
}
