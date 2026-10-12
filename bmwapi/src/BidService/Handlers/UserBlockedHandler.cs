using BidService.Data;
using Contracts;

namespace BidService.Handlers;

/// <summary>Keeps a copy of ChatService's blocks: a blocked user can't bid on the blocker's listings.</summary>
public class UserBlockedHandler(IBidRepository repository)
{
    public Task Handle(UserBlocked message) => repository.SetBlockedAsync(message.Blocker, message.Blocked, true);

    public Task Handle(UserUnblocked message) => repository.SetBlockedAsync(message.Blocker, message.Blocked, false);
}
