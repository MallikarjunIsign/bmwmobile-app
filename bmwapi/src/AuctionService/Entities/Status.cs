namespace AuctionService.Entities;

// Stored as int: keep the existing numeric values (Finished was 1 and is now Sold).
public enum Status
{
    Live = 0,
    Sold = 1,
    ReserveNotMet = 2,
    Unsold = 3
}