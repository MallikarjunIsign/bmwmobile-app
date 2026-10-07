using Contracts;
using Mapster;
using Meilisearch;
using SearchService.Data;
using SearchService.Models;

namespace SearchService.Handlers;

public class TransientSearchException(string message) : Exception(message);

public class AuctionCreatedHandler
{
    private static readonly HashSet<string> FailedOnce = [];

    public async Task Handle(AuctionCreated message, MeilisearchClient client)
    {
        if (message.Make == "fail-once")
        {
            if (FailedOnce.Add(message.Id))
            {
                throw new TransientSearchException($"Simulated failure for {message.Id}");
            }
        }

        if (message.Make == "fail-always")
        {
            throw new TransientSearchException($"Simulated always failure for {message.Id}");
        }

        // Item has no ReservePrice property, so the reserve is dropped here.
        var item = message.Adapt<Item>();
        // Wait until it is indexed, so follow-up events (bids) for this auction find the document.
        var addTask = await client.Auctions().AddDocumentsAsync([item], primaryKey: "id");
        await client.WaitForTaskAsync(addTask.TaskUid);
    }
}