using Meilisearch;
using SearchService.Data;
using SearchService.Models;

namespace SearchService.Services;

public class AuctionSvcHttpClient(HttpClient httpClient, IConfiguration config, 
    MeilisearchClient searchClient)
{
    /// <param name="full">Fetch every auction (re-index), not only the ones updated since the newest indexed one.</param>
    public async Task<List<Item>> GetItemsForSearch(bool full = false)
    {
        var search = await searchClient.Auctions()
            .SearchAsync<Item>(string.Empty, new SearchQuery
            {
                Sort = ["updatedAt:desc"],
                Limit = 1,
            });

        var lastUpdated = full ? null : search.Hits.FirstOrDefault()?.UpdatedAt;

        var dateParam = lastUpdated?.ToString("O") ?? string.Empty;

        var items = await httpClient.GetFromJsonAsync<List<Item>>(
            $"{config["AuctionSvcUrl"]}/api/auctions?date={Uri.EscapeDataString(dateParam)}"
        );
        
        return (items ?? []).Select(x => x.Prepare()).ToList();
    }
}