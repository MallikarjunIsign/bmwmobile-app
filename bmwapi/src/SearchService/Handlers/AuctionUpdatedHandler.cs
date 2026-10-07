using Contracts;
using Mapster;
using Meilisearch;
using SearchService.Data;
using SearchService.Models;

namespace SearchService.Handlers;

public class AuctionUpdatedHandler
{
    /// <summary>
    /// Applies the item, pricing, image and container details from the snapshot. Bid- and end-driven fields
    /// (high bid, reserve met, status, winner, end time) keep the values this index already has, because they
    /// come from BidService's events and may be newer than AuctionService's copy.
    /// </summary>
    public async Task Handle(AuctionUpdated message, MeilisearchClient client)
    {
        var index = client.Auctions();
        var item = message.Adapt<Item>();

        var existing = await TryGetAsync(index, message.Id);
        if (existing != null)
        {
            item.CurrentHighBid = existing.CurrentHighBid ?? item.CurrentHighBid;
            item.ReserveMet = existing.ReserveMet;
            item.Status = existing.Status;
            item.EndReason = existing.EndReason;
            item.SellerDecisionDeadline = existing.SellerDecisionDeadline;
            item.Winner = existing.Winner;
            item.SoldAmount = existing.SoldAmount;
            if (existing.AuctionEnd > item.AuctionEnd && existing.CurrentHighBid != null)
                item.AuctionEnd = existing.AuctionEnd; // extended by anti-sniping
        }

        var addTask = await index.AddDocumentsAsync([item], primaryKey: "id");
        await client.WaitForTaskAsync(addTask.TaskUid);
    }

    private static async Task<Item?> TryGetAsync(Meilisearch.Index index, string id)
    {
        try
        {
            return await index.GetDocumentAsync<Item>(id);
        }
        catch (MeilisearchApiError e) when (e.Code == "document_not_found")
        {
            return null;
        }
    }
}