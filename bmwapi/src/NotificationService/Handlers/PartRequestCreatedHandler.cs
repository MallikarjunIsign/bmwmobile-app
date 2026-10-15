using Contracts;
using Microsoft.AspNetCore.SignalR;
using NotificationService.Hubs;
using NotificationService.Models;

namespace NotificationService.Handlers;

/// <summary>
/// "A buyer is looking for: Front bumper – 2016 Toyota Hilux (Hyderabad)" – live to the connected sellers near the
/// buyer (within the buyer's radius, or in the buyer's country; never another country).
/// </summary>
public class PartRequestCreatedHandler(IHubContext<NotificationHub> hubContext, ConnectedSellers sellers)
{
    public async Task Handle(PartRequestCreated message)
    {
        var connections = sellers.For(message);
        if (connections.Count == 0) return;
        await hubContext.Clients.Clients(connections).SendAsync("Notify",
            new UserNotification("partRequest", message.RequestId, message.Title, null, message.LocationName, message.Buyer));
    }
}
