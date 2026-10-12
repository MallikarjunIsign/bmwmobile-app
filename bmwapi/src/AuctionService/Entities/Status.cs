namespace AuctionService.Entities;

// Stored as int: keep the existing numeric values (Finished was 1 and is now Sold).
public enum Status
{
    Live = 0,
    Sold = 1,
    ReserveNotMet = 2,
    Unsold = 3,
    /// <summary>Not published yet (the seller is still filling in the listing). Never sent to Bid/Search.</summary>
    Draft = 4,
    /// <summary>Cancelled by the seller before any bid.</summary>
    Cancelled = 5
}