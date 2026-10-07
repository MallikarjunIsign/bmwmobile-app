using Contracts;
using Meilisearch;
using SearchService.Data;
using SearchService.Models;

namespace SearchService.Handlers;

public class BidPlacedHandler
{
    public async Task Handle(BidPlaced message, MeilisearchClient client)
    {
        var index = client.Auctions();
        var auction = await index.GetDocumentAsync<Item>(message.AuctionId)
            ?? throw new InvalidOperationException("Could not find auction");

        if (!message.BidStatus.Contains("Accepted")) return;

        // Ignore bids that arrive out of order (lower than the high bid we already have).
        if (auction.CurrentHighBid != null && message.Amount <= auction.CurrentHighBid) return;

        auction.CurrentHighBid = message.Amount;
        // "Accepted" means the bid meets the reserve; "AcceptedBelowReserve" means it doesn't.
        auction.ReserveMet = message.BidStatus == "Accepted";

        var updateTask = await index.UpdateDocumentsAsync([auction]);
        await client.WaitForTaskAsync(updateTask.TaskUid);
    }
}