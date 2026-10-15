using Meilisearch;
using SearchService.Models;
using SearchService.Services;

namespace SearchService.Data;

public class DbInitializer
{
    public static readonly string[] SearchableAttributes =
    [
        "title", "partType", "make", "model", "fitments.make", "fitments.model", "partName", "partNumbers",
        "partNumbersNormalized", "partNumber", "brand", "category", "description", "container.containerNumber"
    ];

    /// <summary>Every attribute the search filters or counts on. Missing ones make Meilisearch refuse the whole search.</summary>
    public static readonly string[] FilterableAttributes =
    [
        "id", "seller", "winner", "status", "auctionEnd", "itemType", "partCategory", "condition", "make", "model",
        "container.id", "container.destinationCountry", "container.destinationPort",
        // Bounding-box pre-filter for "parts within X km".
        "latitude", "longitude",
        // Listing model (design doc §7.4): fitment keys, part numbers, specs, facets.
        "kind", "fitmentMode", "partType", "partTypeId", "category", "brandType", "position", "side",
        "fitKeys", "partNumbersNormalized", "specKeys", "currentPrice", "countryCode", "saleType", "deliveryOptions", "sellerRating",
        // Home / Market: exact radius (_geoRadius), Parts / Vehicles, facet counts, re-index marker.
        "_geo", "listingType", "makes", "models", "years", "indexVersion"
    ];

    public static readonly string[] SortableAttributes =
        ["auctionEnd", "currentHighBid", "currentPrice", "createdAt", "updatedAt", "make", "model", "sellerRating", "sellerRatingCount"];

    /// <summary>
    /// Applies the index settings on EVERY start (not only when the index is new) and checks they are really there:
    /// retries while Meilisearch is still starting, and fails loudly when the settings task fails.
    /// </summary>
    public static Task ConfigureIndex(WebApplication app) => ConfigureIndex(app.Services.GetRequiredService<MeilisearchClient>());

    public static async Task ConfigureIndex(MeilisearchClient client, int attempts = 10, TimeSpan? delay = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ApplySettings(client);
                Console.WriteLine($"Search index settings applied ({FilterableAttributes.Length} filterable attributes)");
                return;
            }
            catch (Exception e) when (attempt < attempts)
            {
                Console.WriteLine($"Search index settings not applied yet ({e.Message}). Retry {attempt}/{attempts - 1}");
                await Task.Delay(delay ?? TimeSpan.FromSeconds(3));
            }
        }
    }

    public static async Task ApplySettings(MeilisearchClient client)
    {
        await DeleteLegacyIndexes(client);

        var index = client.Auctions();
        var settingsTask = await index.UpdateSettingsAsync(new Settings
        {
            SearchableAttributes = SearchableAttributes,
            FilterableAttributes = FilterableAttributes.Select(a => (FilterableAttribute)a).ToList(),
            SortableAttributes = SortableAttributes
        });
        var done = await client.WaitForTaskAsync(settingsTask.TaskUid);
        if (done.Status != TaskInfoStatus.Succeeded)
            throw new InvalidOperationException($"Meilisearch settings task {done.Status}: {done.Error?.GetValueOrDefault("message")}");

        var filterable = FilterableNames(await index.GetFilterableAttributesAsync());
        var missing = FilterableAttributes.Where(a => !filterable.Contains(a)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"Meilisearch is missing filterable attributes: {string.Join(", ", missing)}");
    }

    /// <summary>Attribute names from Meilisearch's answer (plain names or patterns).</summary>
    public static HashSet<string> FilterableNames(IEnumerable<FilterableAttribute> attributes) =>
        attributes.SelectMany(a => a.Attribute is { } name ? [name] : a.AttributePatterns ?? []).ToHashSet();

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

        // Documents indexed before the listing model (no fitment keys yet), with the old delivery options ("Seller
        // ships" / "Buyer arranges", migrated in AuctionService's database), or before the current computed fields
        // (_geo, listingType, makes / models / years – Item.CurrentIndexVersion): fetch everything once again.
        var outdated = await index.SearchAsync<Item>(string.Empty, new SearchQuery
        {
            Filter = "NOT fitKeys EXISTS OR deliveryOptions IN [\"SellerShips\", \"BuyerArranges\"] " +
                     $"OR NOT indexVersion EXISTS OR indexVersion < {Item.CurrentIndexVersion}",
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