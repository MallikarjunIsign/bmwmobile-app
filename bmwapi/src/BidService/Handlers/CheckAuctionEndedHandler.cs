using BidService.Messages;
using BidService.Services;

namespace BidService.Handlers;

public class CheckAuctionEndedHandler(BiddingService biddingService)
{
    public Task Handle(CheckAuctionEnded message) => biddingService.EndAuctionAsync(message.AuctionId);
}

public class CheckSellerDecisionHandler(BiddingService biddingService)
{
    public Task Handle(CheckSellerDecision message) => biddingService.ExpireSellerDecisionAsync(message.AuctionId);
}

public class CheckAuctionEndingSoonHandler(BiddingService biddingService)
{
    public Task Handle(CheckAuctionEndingSoon message) =>
        biddingService.NotifyEndingSoonAsync(message.AuctionId, message.AuctionEnd);
}
