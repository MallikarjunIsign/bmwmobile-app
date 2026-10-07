using Meilisearch;
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
            SearchableAttributes = ["make", "model", "partName", "partNumber", "description", "container.containerNumber"],
            FilterableAttributes =
            [
                "seller", "winner", "status", "auctionEnd", "itemType", "partCategory", "condition",
                "container.id", "container.destinationCountry", "container.destinationPort",
                // Bounding-box pre-filter for "parts within X km".
                "latitude", "longitude"
            ],
            SortableAttributes = ["auctionEnd", "currentHighBid", "createdAt", "updatedAt", "make", "model"]
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

        var items = await auctionSvc.GetItemsForSearch();

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