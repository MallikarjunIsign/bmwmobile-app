namespace BidService.Models;

// Same names and numbers as AuctionService's Status enum.
public enum AuctionStatus
{
    Live = 0,
    Sold = 1,
    ReserveNotMet = 2,
    Unsold = 3
}

public enum EndReason
{
    TimeEnded,
    SellerAccepted,
    SellerAcceptedAfterEnd,
    GracePeriodExpired
}