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

        // The bid count never goes down, even when messages arrive out of order.
        var countChanged = message.BidCount > auction.BidCount;
        if (countChanged) auction.BidCount = message.BidCount;

        // Ignore bids that arrive out of order (lower than the high bid we already have).
        var higher = auction.CurrentHighBid == null || message.Amount > auction.CurrentHighBid;
        if (higher)
        {
            auction.CurrentHighBid = message.Amount;
            // "Accepted" means the bid meets the reserve; "AcceptedBelowReserve" means it doesn't.
            auction.ReserveMet = message.BidStatus == "Accepted";
        }

        if (!higher && !countChanged) return;

        var updateTask = await index.UpdateDocumentsAsync([auction.Prepare()]);
        await client.WaitForTaskAsync(updateTask.TaskUid);
    }
}
