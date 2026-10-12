namespace Contracts;

/// <summary>
/// Published by AuctionService when an order is created (auction won or direct sale bought): ChatService lets the
/// seller message that buyer, and the buyer keeps the conversation after the sale.
/// </summary>
public class OrderCreated
{
    public required string OrderId { get; set; }
    public required string AuctionId { get; set; }
    public required string Seller { get; set; }
    public required string Buyer { get; set; }
}

/// <summary>A chat message was sent (ChatService): NotificationService delivers it live to the recipient.</summary>
public class ChatMessageSent
{
    public required string ConversationId { get; set; }
    public required string MessageId { get; set; }
    public required string ListingId { get; set; }
    public string? ListingTitle { get; set; }
    public required string Sender { get; set; }
    public required string Recipient { get; set; }
    /// <summary>The text (shortened), or "📷 Photo".</summary>
    public required string Preview { get; set; }
    public DateTime SentAt { get; set; }
}

/// <summary>
/// The recipient's app received (Delivered) or opened (Read) a conversation's messages: the sender's ticks change.
/// Every message the sender sent up to <see cref="UpTo"/> has that state.
/// </summary>
public class ChatReceipt
{
    public required string ConversationId { get; set; }
    /// <summary>The sender of the messages, who sees the ticks.</summary>
    public required string NotifyUser { get; set; }
    /// <summary>Delivered or Read.</summary>
    public required string State { get; set; }
    public DateTime UpTo { get; set; }
}

/// <summary>A user blocked another (ChatService): Blocked can't message Blocker or bid on / buy Blocker's listings.</summary>
public class UserBlocked
{
    public required string Blocker { get; set; }
    public required string Blocked { get; set; }
}

public class UserUnblocked
{
    public required string Blocker { get; set; }
    public required string Blocked { get; set; }
}
