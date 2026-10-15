using System.Collections.Concurrent;
using Contracts;

namespace NotificationService.Models;

/// <summary>
/// A personal in-app notification, sent over SignalR as "Notify" to one user only.
/// Types: outbid, newBid, endingSoon, won, lost, sold, endedNoBids, customOrderMatched, orderUpdate, partRequest
/// (to sellers; AuctionId is then the request id and From the buyer, so the app can skip the buyer's own request).
/// The app turns it into text in the user's currency ("You've been outbid – highest bid ₹1,020").
/// Detail: extra text, e.g. the order step ("Out for delivery") for orderUpdate.
/// </summary>
public record UserNotification(string Type, string AuctionId, string? Title, int? Amount, string? Detail = null, string? From = null);

/// <summary>Remembers listing titles from AuctionCreated so notifications can name the listing.</summary>
public class AuctionTitles
{
    private readonly ConcurrentDictionary<string, string> _titles = new();

    public void Remember(AuctionSnapshot auction) => _titles[auction.Id] = !string.IsNullOrWhiteSpace(auction.Title)
        ? auction.Title
        : AuctionCreatedNotification.TitleFor(auction);

    public string? Get(string auctionId) => _titles.GetValueOrDefault(auctionId);
}
