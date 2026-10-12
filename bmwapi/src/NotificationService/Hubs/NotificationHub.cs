using Microsoft.AspNetCore.SignalR;

namespace NotificationService.Hubs;

/// <summary>
/// Live updates for the app. Anyone may connect (bid updates are public); a connection that sends the access token
/// (?access_token=...) is also addressable by username for personal messages such as CustomOrderMatched.
/// </summary>
public class NotificationHub : Hub
{
}

/// <summary>Clients.User(name) uses the "username" claim – the same name the other services use.</summary>
public class UsernameUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst("username")?.Value ?? connection.User?.Identity?.Name;
}
