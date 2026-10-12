using Contracts;
using Microsoft.AspNetCore.SignalR;
using NotificationService.Hubs;

namespace NotificationService.Handlers;

/// <summary>A new chat message – live to the recipient only ("ChatMessage"); the app updates the inbox and the badge.</summary>
public class ChatMessageSentHandler(IHubContext<NotificationHub> hubContext)
{
    public Task Handle(ChatMessageSent message) =>
        hubContext.Clients.User(message.Recipient).SendAsync("ChatMessage", message);
}

/// <summary>"How was your purchase?" – to the buyer, 2 days after completion if they haven't reviewed it.</summary>
public class ReviewRequestedHandler(IHubContext<NotificationHub> hubContext)
{
    public Task Handle(ReviewRequested message) =>
        hubContext.Clients.User(message.Buyer).SendAsync("Notify",
            new NotificationService.Models.UserNotification("reviewRequest", message.AuctionId, message.Title, null, message.OrderId));
}

/// <summary>Delivered / read ticks – live to the sender of the messages ("ChatReceipt").</summary>
public class ChatReceiptHandler(IHubContext<NotificationHub> hubContext)
{
    public Task Handle(ChatReceipt message) =>
        hubContext.Clients.User(message.NotifyUser).SendAsync("ChatReceipt", message);
}
