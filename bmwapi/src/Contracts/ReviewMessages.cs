namespace Contracts;

/// <summary>
/// A seller's rating changed (a review was posted, edited, hidden or shown again). SearchService updates the seller's
/// listings ("⭐ 4.7 (32)", filter "4★ & up", sort "Top-rated sellers"); ChatService shows it in the inbox.
/// </summary>
public class SellerRatingChanged
{
    public required string Seller { get; set; }
    /// <summary>Average seller rating (1–5, one decimal); null without reviews.</summary>
    public double? Average { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// The buyer of a completed order hasn't reviewed it 2 days later: NotificationService reminds them in the app and
/// ChatService sends a push ("How was your purchase?").
/// </summary>
public class ReviewRequested
{
    public required string OrderId { get; set; }
    public required string AuctionId { get; set; }
    public required string Title { get; set; }
    public required string Buyer { get; set; }
    public required string Seller { get; set; }
}
