using BidService.Services;
using Contracts;

namespace BidService.Handlers;

public class AuctionCreatedHandler(BiddingService biddingService)
{
    /// <summary>Stores the auction in the local read model and schedules its end check.</summary>
    public Task Handle(AuctionCreated message) => biddingService.ApplySnapshotAsync(message);
}

public class AuctionUpdatedHandler(BiddingService biddingService)
{
    /// <summary>Picks up pricing / end time changes made before the first bid (reschedules the end check).</summary>
    public Task Handle(AuctionUpdated message) => biddingService.ApplySnapshotAsync(message);
}