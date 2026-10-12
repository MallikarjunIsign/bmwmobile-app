using Contracts;
using Microsoft.AspNetCore.SignalR;
using NotificationService.Hubs;
using NotificationService.Models;

namespace NotificationService.Handlers;

/// <summary>Order tracking ("Shipped", "Ready for pickup", "Delivered" …) – only to the other party of the sale.</summary>
public class OrderStatusChangedHandler(IHubContext<NotificationHub> hubContext)
{
    public Task Handle(OrderStatusChanged message) =>
        hubContext.Clients.User(message.NotifyUser).SendAsync("Notify",
            new UserNotification("orderUpdate", message.AuctionId, message.Title, null, message.StatusLabel));
}
