using AuctionService.Data;
using Contracts;

namespace AuctionService.Handlers;

public class BidPlacedHandler
{
    public async Task Handle(BidPlaced message, AuctionDbContext dbContext)
    {
        Console.WriteLine($"Executed {nameof(BidPlacedHandler)} for {message.Amount} for auction id of {message.AuctionId}");

        var auction = await dbContext.Auctions.FindAsync(message.AuctionId)
            ?? throw new InvalidOperationException("Auction not found");

        var changed = false;
        if (auction.CurrentHighBid == null && message.BidStatus.Contains("Accepted") ||
            message.BidStatus.Contains("Accepted") && message.Amount > auction.CurrentHighBid)
        {
            auction.CurrentHighBid = message.Amount;
            changed = true;
        }

        // Bid count (from BidService; never goes down when messages arrive out of order).
        if (message.BidCount > auction.BidCount)
        {
            auction.BidCount = message.BidCount;
            changed = true;
        }

        if (changed) await dbContext.SaveChangesAsync();
    }
}
