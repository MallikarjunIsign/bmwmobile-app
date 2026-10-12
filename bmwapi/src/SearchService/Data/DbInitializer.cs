using Meilisearch;
using SearchService.Models;
using SearchService.Services;

namespace SearchService.Data;

public class DbInitializer
{
    public static async Task ConfigureIndex(WebApplication app)
    {
        var client = app.Services.GetRequiredService<MeilisearchClient>();

        await DeleteLegacyIndexes(client);

        var index = client.Auctions();

        var settingsTask = await index.UpdateSettingsAsync(new Settings
        {
            SearchableAttributes =
            [
                "title", "partType", "make", "model", "fitments.make", "fitments.model", "partName", "partNumbers",
                "partNumbersNormalized", "partNumber", "brand", "category", "description", "container.containerNumber"
            ],
            FilterableAttributes =
            [
                "id", "seller", "winner", "status", "auctionEnd", "itemType", "partCategory", "condition", "make", "model",
                "container.id", "container.destinationCountry", "container.destinationPort",
                // Bounding-box pre-filter for "parts within X km".
                "latitude", "longitude",
                // Listing model (design doc §7.4): fitment keys, part numbers, specs, facets.
                "kind", "fitmentMode", "partType", "partTypeId", "category", "brandType", "position", "side",
                "fitKeys", "partNumbersNormalized", "specKeys", "currentPrice", "countryCode", "saleType", "deliveryOptions", "sellerRating"
            ],
            SortableAttributes = ["auctionEnd", "currentHighBid", "currentPrice", "createdAt", "updatedAt", "make", "model", "sellerRating", "sellerRatingCount"]
        });
        await client.WaitForTaskAsync(settingsTask.TaskUid);
    }

    // Old indexes still contain the reserve price, so they are dropped rather than migrated.
    private static async Task DeleteLegacyIndexes(MeilisearchClient client)
    {
        foreach (var name in SearchIndex.LegacyNames)
        {
            try
            {
                var task = await client.DeleteIndexAsync(name);
                await client.WaitForTaskAsync(task.TaskUid);
            }
            catch (MeilisearchApiError e) when (e.Code == "index_not_found")
            {
            }
        }
    }

    public static async Task FetchMissingAuctions(WebApplication app)
    {
        var client = app.Services.GetRequiredService<MeilisearchClient>();
        var index = client.Auctions();

        using var scope = app.Services.CreateScope();
        var auctionSvc = scope.ServiceProvider.GetRequiredService<AuctionSvcHttpClient>();

        // Documents indexed before the listing model (no fitment keys yet), or with the old delivery options ("Seller
        // ships" / "Buyer arranges", migrated in AuctionService's database): fetch everything once again.
        var outdated = await index.SearchAsync<Item>(string.Empty, new SearchQuery
        {
            Filter = "NOT fitKeys EXISTS OR deliveryOptions IN [\"SellerShips\", \"BuyerArranges\"]",
            Limit = 1
        });
        var items = await auctionSvc.GetItemsForSearch(full: outdated.Hits.Count > 0);

        if (items.Count == 0)
        {
            Console.WriteLine("Search index is up to date with the AuctionService");
            return;
        }

        var addTask = await index.AddDocumentsAsync(items, primaryKey: "id");
        await client.WaitForTaskAsync(addTask.TaskUid);

        Console.WriteLine($"Search index populated with {items.Count} items");
    }
}