using Contracts;
using Meilisearch;
using SearchService.Data;
using SearchService.Models;

namespace SearchService.Handlers;

/// <summary>
/// A seller's rating changed (AuctionService): every listing of theirs in the index gets the new "⭐ 4.7 (32)", so
/// cards, the "4★ & up" filter and the "Top-rated sellers" sort stay current.
/// </summary>
public class SellerRatingChangedHandler
{
    private const int PageSize = 500;

    public async Task Handle(SellerRatingChanged message, MeilisearchClient client)
    {
        var index = client.Auctions();
        for (var offset = 0; ; offset += PageSize)
        {
            var page = await index.SearchAsync<Item>(string.Empty, new SearchQuery
            {
                Filter = $"seller = {SearchIndex.Quote(message.Seller)}",
                Limit = PageSize,
                Offset = offset
            });
            var items = page.Hits.ToList();
            if (items.Count == 0) return;

            foreach (var item in items)
            {
                item.SellerRating = message.Average;
                item.SellerRatingCount = message.Count;
                item.Prepare();
            }
            var task = await index.UpdateDocumentsAsync(items);
            await client.WaitForTaskAsync(task.TaskUid);
            if (items.Count < PageSize) return;
        }
    }
}
