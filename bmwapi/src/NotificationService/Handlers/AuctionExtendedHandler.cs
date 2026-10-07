using Contracts;
using Microsoft.AspNetCore.SignalR;
using NotificationService.Hubs;

namespace NotificationService.Handlers;

public class AuctionExtendedHandler(IHubContext<NotificationHub> hubContext)
{
    public async Task Handle(AuctionExtended message)
    {
        Console.WriteLine($"=== Auction extended message received ===");

        await hubContext.Clients.All.SendAsync("AuctionExtended", message);
    }
}

public class ContainerUpdatedHandler(IHubContext<NotificationHub> hubContext)
{
    public async Task Handle(ContainerUpdated message)
    {
        Console.WriteLine($"=== Container updated message received ===");

        await hubContext.Clients.All.SendAsync("ContainerUpdated", message);
    }
}