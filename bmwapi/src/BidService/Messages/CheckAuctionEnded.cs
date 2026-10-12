namespace BidService.Messages;

public record CheckAuctionEnded(string AuctionId);

/// <summary>Scheduled for one hour before the end: "auction ending in 1 hour" (ignored if the end moved or it ended).</summary>
public record CheckAuctionEndingSoon(string AuctionId, DateTime AuctionEnd);
