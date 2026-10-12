namespace ChatService.Services;

/// <summary>The listing the conversation is about (header: photo, title, current price, status).</summary>
public record ChatListingDto(string Id, string Title, string? ImageUrl, int Price, string? Currency, string Status, string SaleType,
    bool Deleted);

/// <summary>One inbox row.</summary>
public record ConversationSummaryDto(
    string Id,
    ChatListingDto Listing,
    /// <summary>buying (the caller is the buyer) or selling.</summary>
    string Role,
    string OtherUser,
    string? LastMessage,
    string? LastMessageSender,
    DateTime? LastMessageAt,
    int UnreadCount,
    bool BlockedByMe,
    bool BlockedMe,
    /// <summary>The other person's seller rating ("⭐ 4.7 (32)"); null / 0 without ratings.</summary>
    double? OtherUserRating = null,
    int OtherUserRatingCount = 0);

/// <summary>sent / delivered / read – the ticks on the sender's side.</summary>
public record MessageDto(string Id, string Sender, string? Text, List<PhotoDto> Photos, DateTime SentAt, DateTime? DeliveredAt,
    DateTime? ReadAt, string State);

public record PhotoDto(string Url, string? ThumbnailUrl);

/// <summary>A conversation with a page of messages, oldest first; older pages with ?before=.</summary>
public record ConversationDto(
    string Id,
    ChatListingDto Listing,
    string Role,
    string Buyer,
    string Seller,
    string OtherUser,
    bool BlockedByMe,
    bool BlockedMe,
    /// <summary>The listing was cancelled: read-only.</summary>
    bool ReadOnly,
    /// <summary>Shown in a new chat: keep payments and personal details in the app until the item is sold.</summary>
    bool ShowSafetyTip,
    List<MessageDto> Messages,
    bool HasOlder,
    double? OtherUserRating = null,
    int OtherUserRatingCount = 0);

public class StartConversationDto
{
    public string ListingId { get; set; } = "";
    /// <summary>Only for the seller: the buyer of their sold item ("Message buyer" on the order).</summary>
    public string? Buyer { get; set; }
}

public class SendMessageDto
{
    public string? Text { get; set; }
    public List<PhotoDto> Photos { get; set; } = [];
}

public class ReportDto
{
    /// <summary>spam, abuse, scam or other.</summary>
    public string Reason { get; set; } = "";
    public string? Details { get; set; }
}

public class BlockDto
{
    public string Username { get; set; } = "";
}

public class DeviceDto
{
    public string Token { get; set; } = "";
    /// <summary>android or ios.</summary>
    public string Platform { get; set; } = "android";
}

/// <summary>Sent through the local durable queue after a message is saved: push to the recipient's phones.</summary>
public record SendChatPush(string ConversationId, string Recipient, string Sender, string? ListingTitle, string Preview);
