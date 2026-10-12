using Contracts;
using Microsoft.AspNetCore.SignalR;
using NotificationService.Hubs;
using NotificationService.Models;

namespace NotificationService.Handlers;

/// <summary>"Auction ending in 1 hour" to the seller and everyone who bid.</summary>
public class AuctionEndingSoonHandler(IHubContext<NotificationHub> hubContext, AuctionTitles titles)
{
    public async Task Handle(AuctionEndingSoon message)
    {
        var notification = new UserNotification("endingSoon", message.AuctionId, titles.Get(message.AuctionId), message.CurrentHighBid);
        var users = message.Bidders.Append(message.Seller).Distinct().ToList();
        await hubContext.Clients.Users(users).SendAsync("Notify", notification);
    }
}
