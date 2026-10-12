using Contracts;
using Microsoft.AspNetCore.SignalR;
using NotificationService.Hubs;
using NotificationService.Models;

namespace NotificationService.Handlers;

public class AuctionCreatedHandler(IHubContext<NotificationHub> hubContext, AuctionTitles titles)
{
    public async Task Handle(AuctionCreated message)
    {
        Console.WriteLine($"=== Auction created message received ===");
        titles.Remember(message);

        // Never forward the raw event: it contains the hidden reserve price.
        await hubContext.Clients.All.SendAsync("AuctionCreated", AuctionCreatedNotification.From(message));
    }
}
