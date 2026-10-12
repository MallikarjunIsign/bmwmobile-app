using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ChatService.Entities;

/// <summary>
/// ChatService's copy of a listing, for the conversation header (photo, title, current price, status) and the rules
/// (who the seller is, who bought it). Kept up to date from AuctionService's events; fetched once when unknown.
/// </summary>
[Table("Listings")]
public class ChatListing
{
    [Key, MaxLength(36)]
    public required string Id { get; set; }
    [MaxLength(100)]
    public required string Seller { get; set; }
    [MaxLength(100)]
    public string? SellerName { get; set; }
    [MaxLength(250)]
    public string Title { get; set; } = "";
    [MaxLength(500)]
    public string? ImageUrl { get; set; }
    public int StartingPrice { get; set; }
    public int? CurrentHighBid { get; set; }
    [MaxLength(3)]
    public string? Currency { get; set; }
    /// <summary>Live, Sold, ReserveNotMet, Unsold, ... (AuctionService's status).</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "Live";
    /// <summary>Auction or FixedPrice.</summary>
    [MaxLength(20)]
    public string SaleType { get; set; } = "Auction";
    /// <summary>Cancelled / deleted listing: its chats stay readable but no new messages.</summary>
    public bool Deleted { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ListingBuyer> Buyers { get; set; } = [];

    /// <summary>What a buyer pays now: the highest bid total, or the price.</summary>
    [NotMapped]
    public int Price => SaleType == "FixedPrice" ? StartingPrice : CurrentHighBid ?? StartingPrice;
}

/// <summary>Someone who won / bought the listing (from OrderCreated): the seller may message them.</summary>
[Table("ListingBuyers")]
public class ListingBuyer
{
    [MaxLength(36)]
    public required string ListingId { get; set; }
    [MaxLength(100)]
    public required string Buyer { get; set; }
}

/// <summary>One conversation per (listing, buyer); the seller is the listing's seller.</summary>
[Table("Conversations")]
public class Conversation
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [MaxLength(36)]
    public required string ListingId { get; set; }
    public ChatListing? Listing { get; set; }
    [MaxLength(100)]
    public required string Buyer { get; set; }
    [MaxLength(100)]
    public required string Seller { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Null until the first message (empty conversations aren't listed in the inbox).</summary>
    public DateTime? LastMessageAt { get; set; }
    [MaxLength(120)]
    public string? LastMessagePreview { get; set; }
    [MaxLength(100)]
    public string? LastMessageSender { get; set; }
    public int BuyerUnread { get; set; }
    public int SellerUnread { get; set; }
    public List<Message> Messages { get; set; } = [];

    public bool IsParticipant(string user) => user == Buyer || user == Seller;
    public string Other(string user) => user == Buyer ? Seller : Buyer;
    public int UnreadFor(string user) => user == Buyer ? BuyerUnread : SellerUnread;
}

[Table("Messages")]
public class Message
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [MaxLength(36)]
    public required string ConversationId { get; set; }
    [MaxLength(100)]
    public required string Sender { get; set; }
    [MaxLength(1000)]
    public string? Text { get; set; }
    /// <summary>Up to 5 photos uploaded through AuctionService's /api/media (stored as "/images/…" paths or storage URLs).</summary>
    public List<MessageImage> Images { get; set; } = [];
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    /// <summary>The recipient's app received it.</summary>
    public DateTime? DeliveredAt { get; set; }
    /// <summary>The recipient opened the conversation.</summary>
    public DateTime? ReadAt { get; set; }
}

public class MessageImage
{
    public string Url { get; set; } = "";
    public string? ThumbnailUrl { get; set; }
}

/// <summary>Blocker blocked Blocked: no messages either way, and Blocked can't bid on / buy Blocker's listings.</summary>
[Table("Blocks")]
public class Block
{
    [MaxLength(100)]
    public required string Blocker { get; set; }
    [MaxLength(100)]
    public required string Blocked { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>"Report conversation": spam, abuse, scam or other, for the admins.</summary>
[Table("Reports")]
public class Report
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [MaxLength(36)]
    public required string ConversationId { get; set; }
    [MaxLength(100)]
    public required string Reporter { get; set; }
    [MaxLength(100)]
    public required string ReportedUser { get; set; }
    [MaxLength(20)]
    public required string Reason { get; set; }
    [MaxLength(500)]
    public string? Details { get; set; }
    /// <summary>Open or Closed.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "Open";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A user's seller rating (from AuctionService's SellerRatingChanged), shown in the inbox and chat header.</summary>
[Table("UserRatings")]
public class UserRating
{
    [Key, MaxLength(100)]
    public required string User { get; set; }
    public double? Average { get; set; }
    public int Count { get; set; }
}

/// <summary>A phone that receives push notifications (Firebase Cloud Messaging token).</summary>
[Table("PushDevices")]
public class PushDevice
{
    [Key, MaxLength(300)]
    public required string Token { get; set; }
    [MaxLength(100)]
    public required string User { get; set; }
    /// <summary>android or ios.</summary>
    [MaxLength(10)]
    public string Platform { get; set; } = "android";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
