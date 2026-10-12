using AuctionService.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Data;

public static class AuctionQueryExtensions
{
    /// <summary>Loads everything <see cref="RequestHelpers.AuctionMapper"/> needs.</summary>
    public static IQueryable<Auction> WithDetails(this IQueryable<Auction> query) =>
        query
            .Include(x => x.Item).ThenInclude(x => x.Images)
            .Include(x => x.Item).ThenInclude(x => x.Container)
            .Include(x => x.Item).ThenInclude(x => x.PartType).ThenInclude(x => x!.Category).ThenInclude(x => x!.Parent)
            .Include(x => x.Item).ThenInclude(x => x.PartDetails)
            .Include(x => x.Item).ThenInclude(x => x.Fitments)
            .Include(x => x.Item).ThenInclude(x => x.PartNumbers)
            .Include(x => x.Item).ThenInclude(x => x.Specs)
            .Include(x => x.Item).ThenInclude(x => x.VehicleDetails)
            .AsSplitQuery();

    /// <summary>Published listings only (drafts and cancelled listings are private to the seller).</summary>
    public static IQueryable<Auction> Published(this IQueryable<Auction> query) =>
        query.Where(x => x.Status != Status.Draft && x.Status != Status.Cancelled);
}
