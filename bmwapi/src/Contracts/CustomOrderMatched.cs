namespace Contracts;

/// <summary>A newly published listing matches a buyer's open custom order ("A part matching your request is now available").</summary>
public class CustomOrderMatched
{
    public required string OrderId { get; set; }
    /// <summary>The buyer's username (the notification goes only to them).</summary>
    public required string User { get; set; }
    public required string AuctionId { get; set; }
    public required string Title { get; set; }
    public double? DistanceKm { get; set; }
}
