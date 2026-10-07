using Contracts;
using Meilisearch;
using SearchService.Data;

namespace SearchService.Handlers;

public class AuctionDeletedHandler
{
    public async Task Handle(AuctionDeleted message, MeilisearchClient client)
    {
        await client.Auctions().DeleteOneDocumentAsync(message.Id);
    }
}