using ChatService.Data;
using ChatService.Entities;
using ChatService.Services;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace ChatService.Controllers;

/// <summary>
/// Block user: no messages either way, and the blocked user can't bid on or buy the blocker's listings (BidService and
/// AuctionService get UserBlocked / UserUnblocked).
/// </summary>
[ApiController]
[Authorize]
[Route("api/chat/blocks")]
public class BlocksController(ChatDbContext db, IDbContextOutbox<ChatDbContext> outbox, TimeProvider clock) : ControllerBase
{
    private string Me => User.Identity?.Name ?? throw new UnauthorizedAccessException();

    /// <summary>The users the caller blocked.</summary>
    [HttpGet]
    public async Task<ActionResult<List<string>>> Mine() =>
        await db.Blocks.Where(b => b.Blocker == Me).OrderByDescending(b => b.CreatedAt).Select(b => b.Blocked).ToListAsync();

    [HttpPost]
    public async Task<IActionResult> Block(BlockDto dto)
    {
        var user = dto.Username?.Trim();
        if (string.IsNullOrEmpty(user)) return BadRequest(new { message = "Who do you want to block?" });
        if (user == Me) return BadRequest(new { message = "You can't block yourself" });
        // Only someone the caller has a conversation with (blocking is offered from the chat).
        if (!await db.Conversations.AnyAsync(c => (c.Buyer == Me && c.Seller == user) || (c.Seller == Me && c.Buyer == user)))
            return NotFound(new { message = "You have no chat with this user" });

        if (!await db.Blocks.AnyAsync(b => b.Blocker == Me && b.Blocked == user))
        {
            db.Blocks.Add(new Block { Blocker = Me, Blocked = user, CreatedAt = clock.GetUtcNow().UtcDateTime });
            await outbox.PublishAsync(new UserBlocked { Blocker = Me, Blocked = user });
            await outbox.SaveChangesAndFlushMessagesAsync();
        }
        return NoContent();
    }

    [HttpDelete("{username}")]
    public async Task<IActionResult> Unblock(string username)
    {
        var block = await db.Blocks.FirstOrDefaultAsync(b => b.Blocker == Me && b.Blocked == username);
        if (block is null) return NoContent();
        db.Blocks.Remove(block);
        await outbox.PublishAsync(new UserUnblocked { Blocker = Me, Blocked = username });
        await outbox.SaveChangesAndFlushMessagesAsync();
        return NoContent();
    }
}

/// <summary>Phones that get push notifications for new messages (Firebase token, refreshed on every sign-in).</summary>
[ApiController]
[Authorize]
[Route("api/chat/devices")]
public class DevicesController(ChatDbContext db, TimeProvider clock) : ControllerBase
{
    private string Me => User.Identity?.Name ?? throw new UnauthorizedAccessException();

    [HttpPost]
    public async Task<IActionResult> Register(DeviceDto dto)
    {
        var token = dto.Token?.Trim();
        if (string.IsNullOrEmpty(token) || token.Length > 300) return BadRequest(new { message = "Invalid device token" });
        var platform = dto.Platform?.ToLowerInvariant() == "ios" ? "ios" : "android";

        // A token belongs to the phone: the last user who signed in on it gets the notifications.
        var device = await db.PushDevices.FindAsync(token);
        if (device is null) db.PushDevices.Add(device = new PushDevice { Token = token, User = Me });
        device.User = Me;
        device.Platform = platform;
        device.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>On sign-out: this phone stops getting the user's notifications.</summary>
    [HttpDelete("{token}")]
    public async Task<IActionResult> Remove(string token)
    {
        await db.PushDevices.Where(d => d.Token == token && d.User == Me).ExecuteDeleteAsync();
        return NoContent();
    }
}

/// <summary>Reported conversations for the admins (usernames in the "Admin:Usernames" setting).</summary>
[ApiController]
[Authorize]
[Route("api/chat/admin")]
public class ChatAdminController(ChatDbContext db, IConfiguration configuration) : ControllerBase
{
    private bool IsAdmin => User.Identity?.Name is { } user &&
        (configuration.GetSection("Admin:Usernames").Get<string[]>() ?? []).Contains(user, StringComparer.OrdinalIgnoreCase);

    /// <summary>Open reports with the conversation's last 20 messages.</summary>
    [HttpGet("reports")]
    public async Task<IActionResult> Reports()
    {
        if (!IsAdmin) return Forbid();
        var reports = await db.Reports.AsNoTracking().Where(r => r.Status == "Open").OrderByDescending(r => r.CreatedAt).Take(100).ToListAsync();
        var ids = reports.Select(r => r.ConversationId).Distinct().ToList();
        var messages = await db.Messages.AsNoTracking().Where(m => ids.Contains(m.ConversationId)).ToListAsync();
        return Ok(reports.Select(r => new
        {
            r.Id, r.ConversationId, r.Reporter, r.ReportedUser, r.Reason, r.Details, r.CreatedAt,
            Messages = messages.Where(m => m.ConversationId == r.ConversationId).OrderByDescending(m => m.SentAt).Take(20)
                .OrderBy(m => m.SentAt).Select(m => new { m.Sender, m.Text, Photos = m.Images.Count, m.SentAt })
        }));
    }

    [HttpPost("reports/{id}/close")]
    public async Task<IActionResult> Close(string id)
    {
        if (!IsAdmin) return Forbid();
        var report = await db.Reports.FindAsync(id);
        if (report is null) return NotFound();
        report.Status = "Closed";
        await db.SaveChangesAsync();
        return NoContent();
    }
}
