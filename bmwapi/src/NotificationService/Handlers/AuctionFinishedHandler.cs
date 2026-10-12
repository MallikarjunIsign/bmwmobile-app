using Contracts;
using Microsoft.AspNetCore.SignalR;
using NotificationService.Hubs;
using NotificationService.Models;

namespace NotificationService.Handlers;

public class AuctionFinishedHandler(IHubContext<NotificationHub> hubContext, AuctionTitles titles)
{
    public async Task Handle(AuctionFinished message)
    {
        Console.WriteLine($"=== Auction finished message received ===");

        await hubContext.Clients.All.SendAsync("AuctionFinished", message);

        var title = titles.Get(message.AuctionId);
        if (message.ItemSold && message.Winner is { } winner)
        {
            await hubContext.Clients.User(winner).SendAsync("Notify",
                new UserNotification("won", message.AuctionId, title, message.Amount));
            foreach (var loser in message.Bidders.Where(b => b != winner).Distinct())
                await hubContext.Clients.User(loser).SendAsync("Notify",
                    new UserNotification("lost", message.AuctionId, title, message.Amount));
            await hubContext.Clients.User(message.Seller).SendAsync("Notify",
                new UserNotification("sold", message.AuctionId, title, message.Amount));
        }
        else if (message.Status is "Unsold" && message.Bidders.Count == 0)
        {
            await hubContext.Clients.User(message.Seller).SendAsync("Notify",
                new UserNotification("endedNoBids", message.AuctionId, title, null));
        }
    }
}
