using Contracts;
using Microsoft.AspNetCore.SignalR;
using NotificationService.Hubs;
using NotificationService.Models;

namespace NotificationService.Handlers;

/// <summary>"A part matching your request is now available" – only to the buyer who made the custom order.</summary>
public class CustomOrderMatchedHandler(IHubContext<NotificationHub> hubContext)
{
    public async Task Handle(CustomOrderMatched message)
    {
        Console.WriteLine($"=== Custom order {message.OrderId} matched listing {message.AuctionId} ===");

        await hubContext.Clients.User(message.User).SendAsync("CustomOrderMatched", message);
        await hubContext.Clients.User(message.User).SendAsync("Notify",
            new UserNotification("customOrderMatched", message.AuctionId, message.Title, null));
    }
}
