namespace BidService.Messages;

/// <summary>Scheduled for the end of the ReserveNotMet grace period.</summary>
public record CheckSellerDecision(string AuctionId);