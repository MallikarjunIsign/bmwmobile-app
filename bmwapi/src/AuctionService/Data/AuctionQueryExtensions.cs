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
            .AsSplitQuery();
}