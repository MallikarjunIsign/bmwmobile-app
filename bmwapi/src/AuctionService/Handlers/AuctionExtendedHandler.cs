using AuctionService.Data;
using AuctionService.Entities;
using Contracts;

namespace AuctionService.Handlers;

public class AuctionExtendedHandler
{
    /// <summary>Anti-sniping in BidService moved the end time: keep AuctionEnd in sync.</summary>
    public async Task Handle(AuctionExtended message, AuctionDbContext dbContext)
    {
        var auction = await dbContext.Auctions.FindAsync(message.AuctionId)
            ?? throw new InvalidOperationException("Auction not found");

        // Only ever move a live auction's end later (ignores late or duplicate messages).
        if (auction.Status != Status.Live || message.AuctionEnd <= auction.AuctionEnd) return;

        auction.AuctionEnd = message.AuctionEnd;
        auction.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
    }
}