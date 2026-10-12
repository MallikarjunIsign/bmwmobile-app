using AuctionService.Data;
using AuctionService.Entities;
using AuctionService.RequestHelpers;
using AuctionService.Services;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Handlers;

public class AuctionFinishedHandler
{
    /// <returns>OrderCreated when the auction sold (Wolverine publishes the returned message).</returns>
    public async Task<OrderCreated?> Handle(AuctionFinished message, AuctionDbContext dbContext, AuctionMapper mapper,
        ILogger<AuctionFinishedHandler> logger)
    {
        var auction = await dbContext.Auctions.WithDetails().FirstOrDefaultAsync(x => x.Id == message.AuctionId)
            ??  throw new InvalidOperationException("Auction not found");

        var status = AuctionStatusRules.ResolveStatus(message);

        if (!AuctionStatusRules.CanTransition(auction.Status, status))
        {
            logger.LogInformation("Ignoring AuctionFinished for {AuctionId}: {Current} -> {Next} is not allowed",
                auction.Id, auction.Status, status);
            return null;
        }

        auction.Status = status;
        auction.EndReason = AuctionStatusRules.ResolveEndReason(message);
        auction.SellerDecisionDeadline = status == Status.ReserveNotMet ? message.SellerDecisionDeadline : null;

        Order? order = null;
        if (status == Status.Sold)
        {
            auction.Winner = message.Winner;
            auction.SoldAmount = message.Amount;
            order = CreateOrder(auction, dbContext, mapper);
        }

        // Lets SearchService's startup catch-up pick up the new status too.
        auction.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        // ChatService: the winner and the seller can keep chatting about it.
        return order is null ? null : new OrderCreated { OrderId = order.Id, AuctionId = auction.Id, Seller = order.Seller, Buyer = order.Buyer };
    }

    /// <summary>The sale moves to Orders for both sides: "Sold – awaiting handover".</summary>
    private static Order? CreateOrder(Auction auction, AuctionDbContext dbContext, AuctionMapper mapper)
    {
        if (string.IsNullOrEmpty(auction.Winner) || dbContext.Orders.Any(o => o.AuctionId == auction.Id)) return null;

        var order = OrderTracking.NewOrder(auction, auction.Winner, 1,
            auction.SoldAmount ?? auction.CurrentHighBid ?? auction.StartingPrice, mapper.CoverUrl(auction));
        dbContext.Orders.Add(order);
        return order;
    }
}
