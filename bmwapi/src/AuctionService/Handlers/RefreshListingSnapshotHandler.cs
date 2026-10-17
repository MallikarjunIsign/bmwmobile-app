using AuctionService.Data;
using AuctionService.Entities;
using AuctionService.RequestHelpers;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Handlers;

/// <summary>
/// "This listing changed – tell Search and Bid": the full AuctionUpdated snapshot (images, fitments, part numbers, specs)
/// is built here, after the request has answered, instead of loading all of that inside checkout. Durable (saved with
/// the change, in the same transaction), so it isn't lost; always the listing's state when it runs.
/// </summary>
public record RefreshListingSnapshot(string AuctionId);

public class RefreshListingSnapshotHandler
{
    public async Task<AuctionUpdated?> Handle(RefreshListingSnapshot message, AuctionDbContext db, AuctionMapper mapper, CancellationToken ct)
    {
        var auction = await db.Auctions.AsNoTracking().WithDetails().FirstOrDefaultAsync(a => a.Id == message.AuctionId, ct);
        // Drafts are never sent to Bid / Search.
        if (auction is null || auction.Status == Status.Draft) return null;
        return mapper.ToUpdated(auction);
    }
}
