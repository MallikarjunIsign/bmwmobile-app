using ChatService.Data;
using ChatService.Entities;
using ChatService.Services;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine.EntityFrameworkCore;

namespace ChatService.Controllers;

/// <summary>
/// Buyer–seller chat. One conversation per (listing, buyer); a buyer starts it from the listing, the seller replies
/// (or messages the buyer of their sold item). Only the two participants can read it – checked on every request.
/// Messages are kept; a cancelled listing's chats become read-only. Blocked users can't send.
/// </summary>
[ApiController]
[Authorize]
[Route("api/chat")]
public class ConversationsController(ChatDbContext db, IDbContextOutbox<ChatDbContext> outbox, ListingDirectory listings,
    IOptions<ChatOptions> options, TimeProvider clock) : ControllerBase
{
    private string Me => User.Identity?.Name ?? throw new UnauthorizedAccessException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private ChatOptions Options => options.Value;

    /// <summary>The inbox: conversations with messages, newest first. role = all (default), buying or selling.</summary>
    [HttpGet("conversations")]
    public async Task<ActionResult<List<ConversationSummaryDto>>> Inbox([FromQuery] string? role)
    {
        var me = Me;
        var query = db.Conversations.Include(c => c.Listing).Where(c => c.LastMessageAt != null);
        query = role?.ToLowerInvariant() switch
        {
            "buying" => query.Where(c => c.Buyer == me),
            "selling" => query.Where(c => c.Seller == me),
            _ => query.Where(c => c.Buyer == me || c.Seller == me)
        };
        var conversations = await query.OrderByDescending(c => c.LastMessageAt).Take(200).ToListAsync();

        // Opening the inbox means the app received these messages.
        await MarkDeliveredAsync(conversations.Where(c => c.UnreadFor(me) > 0).ToList());

        var others = conversations.Select(c => c.Other(me)).Distinct().ToList();
        var blocks = await BlocksWithAsync(others);
        var ratings = await db.UserRatings.AsNoTracking().Where(r => others.Contains(r.User)).ToDictionaryAsync(r => r.User);
        return conversations.Select(c =>
        {
            var rating = ratings.GetValueOrDefault(c.Other(me));
            return new ConversationSummaryDto(
                c.Id, ToDto(c.Listing!), c.Buyer == me ? "buying" : "selling", c.Other(me), c.LastMessagePreview, c.LastMessageSender,
                c.LastMessageAt, c.UnreadFor(me), blocks.Contains((me, c.Other(me))), blocks.Contains((c.Other(me), me)),
                rating?.Average, rating?.Count ?? 0);
        }).ToList();
    }

    /// <summary>Unread messages over all conversations (the badge on the Messages icon).</summary>
    [HttpGet("unread")]
    public async Task<ActionResult<object>> Unread()
    {
        var me = Me;
        var count = await db.Conversations.Where(c => c.Buyer == me).SumAsync(c => c.BuyerUnread) +
                    await db.Conversations.Where(c => c.Seller == me).SumAsync(c => c.SellerUnread);
        return new { count };
    }

    /// <summary>
    /// Opens the conversation about a listing (creating it if needed). Buyers: "Chat with seller". The seller can't
    /// start a chat with a stranger – only open one a buyer started, or message the buyer of their sold item.
    /// </summary>
    [HttpPost("conversations")]
    public async Task<ActionResult<ConversationDto>> Start(StartConversationDto dto)
    {
        var me = Me;
        ChatListing? listing;
        try
        {
            listing = await listings.GetAsync(dto.ListingId);
        }
        catch (ListingUnavailableException)
        {
            return StatusCode(503, new { message = "The listing can't be loaded right now – try again" });
        }
        if (listing is null) return NotFound(new { message = "Listing not found" });

        string buyer;
        if (listing.Seller == me)
        {
            if (string.IsNullOrWhiteSpace(dto.Buyer))
                return BadRequest(new { message = "Buyers start chats – you can reply to the chats you receive" });
            if (dto.Buyer == me) return BadRequest(new { message = "You can't chat with yourself" });
            buyer = dto.Buyer;
            var exists = await db.Conversations.AnyAsync(c => c.ListingId == listing.Id && c.Buyer == buyer);
            if (!exists && listing.Buyers.All(b => b.Buyer != buyer))
                return StatusCode(403, new { message = "You can only message buyers who contacted you or bought this item" });
        }
        else
        {
            buyer = me;
        }

        var conversation = await db.Conversations.Include(c => c.Listing).FirstOrDefaultAsync(c => c.ListingId == listing.Id && c.Buyer == buyer);
        if (conversation is null)
        {
            if (listing.Deleted) return BadRequest(new { message = "This listing was removed" });
            if (await db.Blocks.AnyAsync(b => b.Blocker == listing.Seller && b.Blocked == buyer))
                return StatusCode(403, new { message = "You can't message this seller" });

            conversation = new Conversation { ListingId = listing.Id, Listing = listing, Buyer = buyer, Seller = listing.Seller, CreatedAt = Now };
            db.Conversations.Add(conversation);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Started twice at the same moment: use the one that won.
                db.ChangeTracker.Clear();
                conversation = await db.Conversations.Include(c => c.Listing).FirstAsync(c => c.ListingId == listing.Id && c.Buyer == buyer);
            }
        }

        return await PageAsync(conversation, null);
    }

    /// <summary>The conversation with its newest messages (oldest first); ?before= loads older ones.</summary>
    [HttpGet("conversations/{id}")]
    public async Task<ActionResult<ConversationDto>> Get(string id, [FromQuery] DateTime? before)
    {
        var (conversation, error) = await LoadAsync(id);
        if (error != null) return error;
        return await PageAsync(conversation!, before);
    }

    [HttpPost("conversations/{id}/messages")]
    public async Task<ActionResult<MessageDto>> Send(string id, SendMessageDto dto)
    {
        var (conversation, error) = await LoadAsync(id);
        if (error != null) return error;
        var me = Me;
        var other = conversation!.Other(me);

        var text = dto.Text?.Trim();
        if (string.IsNullOrEmpty(text) && dto.Photos.Count == 0) return BadRequest(new { message = "Write a message or add a photo" });
        if (text?.Length > Options.MaxTextLength) return BadRequest(new { message = $"A message can have at most {Options.MaxTextLength} characters" });
        if (dto.Photos.Count > Options.MaxPhotos) return BadRequest(new { message = $"At most {Options.MaxPhotos} photos per message" });

        var photos = new List<MessageImage>();
        foreach (var photo in dto.Photos)
        {
            var url = PhotoUrls.Normalize(photo.Url, Options.RemotePhotoPrefixes);
            if (url is null) return BadRequest(new { message = "Photos must be uploaded through the app" });
            var thumbnail = photo.ThumbnailUrl is null ? null : PhotoUrls.Normalize(photo.ThumbnailUrl, Options.RemotePhotoPrefixes);
            photos.Add(new MessageImage { Url = url, ThumbnailUrl = thumbnail });
        }

        if (conversation.Listing!.Deleted) return BadRequest(new { message = "This listing was removed – the chat is read-only" });
        if (await db.Blocks.AnyAsync(b => b.Blocker == other && b.Blocked == me))
            return StatusCode(403, new { message = "You can't message this user" });
        if (await db.Blocks.AnyAsync(b => b.Blocker == me && b.Blocked == other))
            return BadRequest(new { message = "You blocked this user – unblock them to send messages" });

        var now = Now;
        var recent = await db.Messages.CountAsync(m => m.Sender == me && m.SentAt > now.AddMinutes(-1));
        if (recent >= Options.MessagesPerMinute)
            return StatusCode(429, new { message = "You're sending messages too fast – wait a minute" });

        var message = new Message { ConversationId = conversation.Id, Sender = me, Text = string.IsNullOrEmpty(text) ? null : text, Images = photos, SentAt = now };
        db.Messages.Add(message);

        var preview = Preview(message);
        conversation.LastMessageAt = now;
        conversation.LastMessagePreview = preview;
        conversation.LastMessageSender = me;
        if (other == conversation.Buyer) conversation.BuyerUnread++;
        else conversation.SellerUnread++;

        // Live to the recipient (NotificationService) and a push to their phones; both only if this commits.
        await outbox.PublishAsync(new ChatMessageSent
        {
            ConversationId = conversation.Id, MessageId = message.Id, ListingId = conversation.ListingId, ListingTitle = conversation.Listing.Title,
            Sender = me, Recipient = other, Preview = preview, SentAt = now
        });
        await outbox.PublishAsync(new SendChatPush(conversation.Id, other, me, conversation.Listing.Title, preview));
        await outbox.SaveChangesAndFlushMessagesAsync();

        return ToDto(message);
    }

    /// <summary>The caller opened the conversation: their incoming messages are read (the sender sees blue ticks).</summary>
    [HttpPost("conversations/{id}/read")]
    public async Task<IActionResult> Read(string id)
    {
        var (conversation, error) = await LoadAsync(id);
        if (error != null) return error;
        await MarkReadAsync(conversation!);
        return NoContent();
    }

    /// <summary>The app received a live message: delivered ticks for the sender.</summary>
    [HttpPost("conversations/{id}/delivered")]
    public async Task<IActionResult> Delivered(string id)
    {
        var (conversation, error) = await LoadAsync(id);
        if (error != null) return error;
        await MarkDeliveredAsync([conversation!]);
        return NoContent();
    }

    /// <summary>Report conversation: spam, abuse, scam or other (for the admins).</summary>
    [HttpPost("conversations/{id}/report")]
    public async Task<IActionResult> Report(string id, ReportDto dto)
    {
        var (conversation, error) = await LoadAsync(id);
        if (error != null) return error;
        var reason = dto.Reason?.Trim().ToLowerInvariant();
        if (reason is not ("spam" or "abuse" or "scam" or "other"))
            return BadRequest(new { message = "Choose a reason: spam, abuse, scam or other" });
        var details = dto.Details?.Trim();
        if (details?.Length > 500) return BadRequest(new { message = "Keep the details under 500 characters" });

        db.Reports.Add(new Report
        {
            ConversationId = conversation!.Id, Reporter = Me, ReportedUser = conversation.Other(Me), Reason = reason,
            Details = string.IsNullOrEmpty(details) ? null : details, CreatedAt = Now
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ── helpers ────────────────────────────────────────────────────────

    /// <summary>The conversation, if the caller is one of its two participants (404 otherwise – it doesn't exist for them).</summary>
    private async Task<(Conversation? Conversation, ActionResult? Error)> LoadAsync(string id)
    {
        var conversation = await db.Conversations.Include(c => c.Listing).FirstOrDefaultAsync(c => c.Id == id);
        if (conversation is null || !conversation.IsParticipant(Me)) return (null, NotFound());
        return (conversation, null);
    }

    private async Task<ActionResult<ConversationDto>> PageAsync(Conversation conversation, DateTime? before)
    {
        var me = Me;
        var size = Options.PageSize;
        var query = db.Messages.AsNoTracking().Where(m => m.ConversationId == conversation.Id);
        if (before is not null)
        {
            var cutoff = DateTime.SpecifyKind(before.Value.ToUniversalTime(), DateTimeKind.Utc);
            query = query.Where(m => m.SentAt < cutoff);
        }
        var page = await query.OrderByDescending(m => m.SentAt).Take(size + 1).ToListAsync();

        // Opening it delivers the incoming messages (reading happens with POST …/read while it's on screen).
        if (conversation.UnreadFor(me) > 0) await MarkDeliveredAsync([conversation]);

        var other = conversation.Other(me);
        var blocks = await BlocksWithAsync([other]);
        var rating = await db.UserRatings.AsNoTracking().FirstOrDefaultAsync(r => r.User == other);
        return new ConversationDto(
            conversation.Id, ToDto(conversation.Listing!), conversation.Buyer == me ? "buying" : "selling", conversation.Buyer,
            conversation.Seller, other, blocks.Contains((me, other)), blocks.Contains((other, me)), conversation.Listing!.Deleted,
            conversation.Listing.Status is "Live" or "ReserveNotMet",
            page.Take(size).OrderBy(m => m.SentAt).Select(ToDto).ToList(), page.Count > size, rating?.Average, rating?.Count ?? 0);
    }

    private async Task MarkDeliveredAsync(List<Conversation> conversations)
    {
        if (conversations.Count == 0) return;
        var me = Me;
        var ids = conversations.Select(c => c.Id).ToList();
        var now = Now;
        var pending = await db.Messages.Where(m => ids.Contains(m.ConversationId) && m.Sender != me && m.DeliveredAt == null).ToListAsync();
        if (pending.Count == 0) return;
        foreach (var m in pending) m.DeliveredAt = now;
        foreach (var conversationId in pending.Select(m => m.ConversationId).Distinct())
        {
            var c = conversations.First(x => x.Id == conversationId);
            await outbox.PublishAsync(new ChatReceipt { ConversationId = c.Id, NotifyUser = c.Other(me), State = "Delivered", UpTo = now });
        }
        await outbox.SaveChangesAndFlushMessagesAsync();
    }

    private async Task MarkReadAsync(Conversation conversation)
    {
        var me = Me;
        var now = Now;
        var unread = await db.Messages.Where(m => m.ConversationId == conversation.Id && m.Sender != me && m.ReadAt == null).ToListAsync();
        foreach (var m in unread)
        {
            m.DeliveredAt ??= now;
            m.ReadAt = now;
        }
        if (me == conversation.Buyer) conversation.BuyerUnread = 0;
        else conversation.SellerUnread = 0;
        if (unread.Count > 0)
            await outbox.PublishAsync(new ChatReceipt { ConversationId = conversation.Id, NotifyUser = conversation.Other(me), State = "Read", UpTo = now });
        await outbox.SaveChangesAndFlushMessagesAsync();
    }

    /// <summary>(blocker, blocked) pairs between the caller and these users, both ways.</summary>
    private async Task<HashSet<(string, string)>> BlocksWithAsync(IEnumerable<string> others)
    {
        var me = Me;
        var list = others.Distinct().ToList();
        var rows = await db.Blocks.AsNoTracking()
            .Where(b => (b.Blocker == me && list.Contains(b.Blocked)) || (b.Blocked == me && list.Contains(b.Blocker)))
            .Select(b => new { b.Blocker, b.Blocked }).ToListAsync();
        return rows.Select(r => (r.Blocker, r.Blocked)).ToHashSet();
    }

    private ChatListingDto ToDto(ChatListing l) =>
        new(l.Id, l.Title, l.ImageUrl is null ? null : Options.PhotoUrl(l.ImageUrl), l.Price, l.Currency, l.Status, l.SaleType, l.Deleted);

    private MessageDto ToDto(Message m) => new(
        m.Id, m.Sender, m.Text, m.Images.Select(i => new PhotoDto(Options.PhotoUrl(i.Url), i.ThumbnailUrl is null ? null : Options.PhotoUrl(i.ThumbnailUrl))).ToList(),
        m.SentAt, m.DeliveredAt, m.ReadAt, m.ReadAt != null ? "read" : m.DeliveredAt != null ? "delivered" : "sent");

    /// <summary>The inbox line: the text (shortened) or "📷 Photo".</summary>
    public static string Preview(Message m)
    {
        if (string.IsNullOrEmpty(m.Text)) return m.Images.Count > 1 ? $"📷 {m.Images.Count} photos" : "📷 Photo";
        return m.Text.Length <= 100 ? m.Text : m.Text[..100] + "…";
    }
}
