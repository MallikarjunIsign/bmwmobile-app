using AuctionService.Data;
using AuctionService.Entities;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Handlers;

/// <summary>Keeps a copy of ChatService's blocks: a blocked user can't buy the blocker's direct sales.</summary>
public class UserBlockedHandler
{
    public async Task Handle(UserBlocked message, AuctionDbContext dbContext)
    {
        if (await dbContext.UserBlocks.AnyAsync(b => b.Blocker == message.Blocker && b.Blocked == message.Blocked)) return;
        dbContext.UserBlocks.Add(new UserBlock { Blocker = message.Blocker, Blocked = message.Blocked });
        await dbContext.SaveChangesAsync();
    }

    public async Task Handle(UserUnblocked message, AuctionDbContext dbContext) =>
        await dbContext.UserBlocks.Where(b => b.Blocker == message.Blocker && b.Blocked == message.Blocked).ExecuteDeleteAsync();
}
