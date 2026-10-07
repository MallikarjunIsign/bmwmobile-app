using AuctionService.Data;
using AuctionService.Entities;
using Contracts;

namespace AuctionService.Handlers;

public class AuctionFinishedHandler
{
    public async Task Handle(AuctionFinished message, AuctionDbContext dbContext, ILogger<AuctionFinishedHandler> logger)
    {
        var auction = await dbContext.Auctions.FindAsync(message.AuctionId)
            ??  throw new InvalidOperationException("Auction not found");

        var status = AuctionStatusRules.ResolveStatus(message);

        if (!AuctionStatusRules.CanTransition(auction.Status, status))
        {
            logger.LogInformation("Ignoring AuctionFinished for {AuctionId}: {Current} -> {Next} is not allowed",
                auction.Id, auction.Status, status);
            return;
        }

        auction.Status = status;
        auction.EndReason = AuctionStatusRules.ResolveEndReason(message);
        auction.SellerDecisionDeadline = status == Status.ReserveNotMet ? message.SellerDecisionDeadline : null;

        if (status == Status.Sold)
        {
            auction.Winner = message.Winner;
            auction.SoldAmount = message.Amount;
        }

        // Lets SearchService's startup catch-up pick up the new status too.
        auction.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();
    }
}