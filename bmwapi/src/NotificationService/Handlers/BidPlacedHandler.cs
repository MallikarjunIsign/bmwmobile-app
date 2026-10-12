using Contracts;
using Microsoft.AspNetCore.SignalR;
using NotificationService.Hubs;
using NotificationService.Models;

namespace NotificationService.Handlers;

public class BidPlacedHandler(IHubContext<NotificationHub> hubContext, AuctionTitles titles)
{
    public async Task Handle(BidPlaced message)
    {
        Console.WriteLine($"=== Bid placed message received ===");

        // Everyone viewing the listing: price, next bid and bid count update live.
        await hubContext.Clients.All.SendAsync("BidPlaced", message);

        if (!message.BidStatus.Contains("Accepted")) return;
        var title = titles.Get(message.AuctionId);

        // "You've been outbid"
        if (message.PreviousHighBidder is { } previous && previous != message.Bidder)
            await hubContext.Clients.User(previous).SendAsync("Notify",
                new UserNotification("outbid", message.AuctionId, title, message.Amount));

        // Seller: "New bid on your listing"
        if (message.Seller is { } seller)
            await hubContext.Clients.User(seller).SendAsync("Notify",
                new UserNotification("newBid", message.AuctionId, title, message.Amount));
    }
}
