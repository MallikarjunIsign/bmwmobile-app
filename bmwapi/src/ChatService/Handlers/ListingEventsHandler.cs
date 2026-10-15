using ChatService.Data;
using ChatService.Entities;
using ChatService.Services;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace ChatService.Handlers;

/// <summary>
/// Keeps the chat's copy of listings current from AuctionService / BidService events, so the conversation header shows
/// the current price and status, and the seller can message the buyer of a sold item.
/// </summary>
public class ListingEventsHandler
{
    public Task Handle(AuctionCreated message, ChatDbContext db) => UpsertAsync(message, db);

    public Task Handle(AuctionUpdated message, ChatDbContext db) => UpsertAsync(message, db);

    /// <summary>Cancelled / deleted: the chats stay, read-only.</summary>
    public async Task Handle(AuctionDeleted message, ChatDbContext db)
    {
        var listing = await db.Listings.FindAsync(message.Id);
        if (listing is null) return;
        listing.Deleted = true;
        listing.Status = "Cancelled";
        listing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task Handle(BidPlaced message, ChatDbContext db)
    {
        if (!message.BidStatus.Contains("Accepted")) return;
        var listing = await db.Listings.FindAsync(message.AuctionId);
        if (listing is null) return;
        listing.CurrentHighBid = ListingDirectory.Max(listing.CurrentHighBid, message.Amount);
        listing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task Handle(AuctionFinished message, ChatDbContext db)
    {
        var listing = await db.Listings.Include(l => l.Buyers).FirstOrDefaultAsync(l => l.Id == message.AuctionId);
        if (listing is null) return;
        listing.Status = message.Status ?? (message.ItemSold ? "Sold" : "Unsold");
        if (message.ItemSold && message.Amount is { } amount) listing.CurrentHighBid = ListingDirectory.Max(listing.CurrentHighBid, amount);
        listing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>A sale (auction won or direct sale bought): the seller may message this buyer.</summary>
    public async Task Handle(OrderCreated message, ChatDbContext db)
    {
        if (!await db.Listings.AnyAsync(l => l.Id == message.AuctionId))
            db.Listings.Add(new ChatListing { Id = message.AuctionId, Seller = message.Seller, Status = "Sold" });
        if (!await db.ListingBuyers.AnyAsync(b => b.ListingId == message.AuctionId && b.Buyer == message.Buyer))
            db.ListingBuyers.Add(new ListingBuyer { ListingId = message.AuctionId, Buyer = message.Buyer });
        await db.SaveChangesAsync();
    }

    private static async Task UpsertAsync(AuctionSnapshot s, ChatDbContext db)
    {
        // Drafts never reach the chat (they aren't published), but be safe.
        if (s.Status == "Draft") return;
        var listing = await db.Listings.FindAsync(s.Id);
        if (listing is null) db.Listings.Add(listing = new ChatListing { Id = s.Id, Seller = s.Seller });
        ListingDirectory.Apply(listing, new ListingInfo(s.Id, s.Seller, s.SellerName, s.Title ?? "", s.ThumbnailUrl ?? (s.ImageUrl.Length > 0 ? s.ImageUrl : null),
            s.StartingPrice, s.CurrentHighBid, s.Currency, s.Status, s.SaleType, s.Winner));
        await db.SaveChangesAsync();
    }
}

/// <summary>Ratings (AuctionService) for the inbox and the chat header, and the review reminder as a push.</summary>
public class RatingEventsHandler
{
    public async Task Handle(SellerRatingChanged message, ChatDbContext db)
    {
        var rating = await db.UserRatings.FindAsync(message.Seller);
        if (rating is null) db.UserRatings.Add(rating = new UserRating { User = message.Seller });
        rating.Average = message.Average;
        rating.Count = message.Count;
        await db.SaveChangesAsync();
    }

    /// <summary>"How was your purchase?" on the buyer's phones (2 days after completion, if not reviewed).</summary>
    public async Task Handle(ReviewRequested message, ChatDbContext db, IPushSender push)
    {
        var tokens = await db.PushDevices.Where(d => d.User == message.Buyer).Select(d => d.Token).ToListAsync();
        if (tokens.Count == 0) return;
        var invalid = await push.SendAsync(tokens, new PushMessage("How was your purchase?", $"Rate {message.Title} and the seller",
            new Dictionary<string, string> { ["type"] = "review", ["orderId"] = message.OrderId }));
        if (invalid.Count > 0) await db.PushDevices.Where(d => invalid.Contains(d.Token)).ExecuteDeleteAsync();
    }
}

/// <summary>
/// Custom orders by push (the app also gets them live while open): a buyer's request goes to every seller's phones;
/// a matching listing goes to the buyer's phones.
/// </summary>
public class PartRequestPushHandler
{
    /// <summary>
    /// "🔎 A buyer is looking for a part" → the sellers near the buyer: within the buyer's radius, or in the buyer's
    /// country when the buyer gave no location; never another country (not the buyer, if they sell too).
    /// </summary>
    public async Task Handle(PartRequestCreated message, ChatDbContext db, IPushSender push)
    {
        var sellers = await db.PushDevices.Where(d => d.IsSeller && d.User != message.Buyer).ToListAsync();
        var tokens = sellers.Where(d => PartRequestAudience.Reaches(message, d.CountryCode, d.Latitude, d.Longitude)).Select(d => d.Token).ToList();
        if (tokens.Count == 0) return;
        var where = string.IsNullOrWhiteSpace(message.LocationName) ? "" : $" in {message.LocationName}";
        var invalid = await push.SendAsync(tokens, new PushMessage("🔎 A buyer is looking for a part", $"{message.Title}{where}. Have one? List it.",
            new Dictionary<string, string> { ["type"] = "partRequest", ["requestId"] = message.RequestId }));
        if (invalid.Count > 0) await db.PushDevices.Where(d => invalid.Contains(d.Token)).ExecuteDeleteAsync();
    }

    /// <summary>"A part matching your request is now available" → the buyer's phones.</summary>
    public async Task Handle(CustomOrderMatched message, ChatDbContext db, IPushSender push)
    {
        var tokens = await db.PushDevices.Where(d => d.User == message.User).Select(d => d.Token).ToListAsync();
        if (tokens.Count == 0) return;
        var invalid = await push.SendAsync(tokens, new PushMessage("✅ The part you asked for is available", message.Title,
            new Dictionary<string, string> { ["type"] = "customOrderMatched", ["auctionId"] = message.AuctionId }));
        if (invalid.Count > 0) await db.PushDevices.Where(d => invalid.Contains(d.Token)).ExecuteDeleteAsync();
    }
}

/// <summary>After a message is saved: a push notification to the recipient's phones (if push is set up).</summary>
public class SendChatPushHandler
{
    public async Task Handle(SendChatPush message, ChatDbContext db, IPushSender push)
    {
        var tokens = await db.PushDevices.Where(d => d.User == message.Recipient).Select(d => d.Token).ToListAsync();
        if (tokens.Count == 0) return;

        var title = $"New message from {message.Sender}";
        var body = string.IsNullOrEmpty(message.ListingTitle) ? message.Preview : $"About {message.ListingTitle}: {message.Preview}";
        var invalid = await push.SendAsync(tokens, new PushMessage(title, body, new Dictionary<string, string>
        {
            ["type"] = "chat", ["conversationId"] = message.ConversationId
        }));
        if (invalid.Count > 0) await db.PushDevices.Where(d => invalid.Contains(d.Token)).ExecuteDeleteAsync();
    }
}
