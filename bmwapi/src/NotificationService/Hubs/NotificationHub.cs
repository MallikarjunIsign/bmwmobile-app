using System.Collections.Concurrent;
using Contracts;
using Microsoft.AspNetCore.SignalR;

namespace NotificationService.Hubs;

/// <summary>
/// Live updates for the app. Anyone may connect (bid updates are public); a connection that sends the access token
/// (?access_token=...) is also addressable by username for personal messages such as CustomOrderMatched.
/// </summary>
public class NotificationHub(ConnectedSellers sellers) : Hub
{
    /// <summary>Signed-in sellers (token role "seller") are remembered with their location for buyers' part requests.</summary>
    public override async Task OnConnectedAsync()
    {
        if (AppRoles.IsSeller(Context.User))
        {
            var (country, lat, lon) = PartRequestAudience.FromClaims(Context.User);
            sellers.Add(Context.ConnectionId, new ConnectedSeller(Context.UserIdentifier ?? "", country, lat, lon));
        }
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        sellers.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

public record ConnectedSeller(string Username, string? Country, double? Latitude, double? Longitude);

/// <summary>The sellers connected right now (connection id → who and where), for location-based notifications.</summary>
public class ConnectedSellers
{
    private readonly ConcurrentDictionary<string, ConnectedSeller> _connections = new();

    public void Add(string connectionId, ConnectedSeller seller) => _connections[connectionId] = seller;
    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    /// <summary>Connections of the sellers this request reaches (not the buyer themself).</summary>
    public List<string> For(PartRequestCreated request) => _connections
        .Where(c => c.Value.Username != request.Buyer && PartRequestAudience.Reaches(request, c.Value.Country, c.Value.Latitude, c.Value.Longitude))
        .Select(c => c.Key)
        .ToList();
}

/// <summary>Clients.User(name) uses the "username" claim – the same name the other services use.</summary>
public class UsernameUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst("username")?.Value ?? connection.User?.Identity?.Name;
}
