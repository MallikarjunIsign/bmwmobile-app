using System.Net;
using System.Text.Json;
using Alba;
using Contracts;
using Meilisearch;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SearchService.Data;
using SearchService.Endpoints;
using SearchService.Models;
using SearchService.Services;
using Wolverine;

namespace SearchService.Tests;

/// <summary>
/// The filters the app sends with a search (suggestion chips, category tiles, Parts / Vehicles, Auction / Direct sale),
/// alone and combined with text: every result matches every filter, and the counts are the results.
/// </summary>
[Collection("search-service")]
public class SearchFilterTests(AppFixture fixture) : IAsyncLifetime
{
    private const string Near = "lat=17.385&lon=78.4867&radiusKm=60";

    private static Item Listing(string id, Action<Item> configure) => TestData.CreateItem(x =>
    {
        x.Id = id;
        x.ItemType = "Part";
        x.Make = "";
        x.Model = "";
        x.Year = 0;
        x.Latitude = 17.44;
        x.Longitude = 78.45;
        x.CountryCode = "IN";
        x.StartingPrice = 1000;
        x.CurrentHighBid = null;
        configure(x);
    }).Prepare();

    private static readonly FitmentInfo Hilux = new() { YearFrom = 2016, YearTo = 2020, Make = "Toyota", Model = "Hilux" };
    private static readonly FitmentInfo Swift = new() { YearFrom = 2018, YearTo = 2023, Make = "Maruti Suzuki", Model = "Swift" };

    private readonly Item[] _items =
    [
        Listing("bumper-auction", x =>
        {
            x.Kind = "VehicleSpecificPart"; x.FitmentMode = "VehicleSpecific"; x.PartType = "Bumper"; x.Category = "Body – Exterior";
            x.Title = "Toyota Hilux Front Bumper"; x.Position = "Front"; x.Fitments = [Hilux]; x.SaleType = "Auction";
        }),
        Listing("bumper-direct", x =>
        {
            x.Kind = "VehicleSpecificPart"; x.FitmentMode = "VehicleSpecific"; x.PartType = "Bumper"; x.Category = "Body – Exterior";
            x.Title = "Maruti Suzuki Swift Front Bumper"; x.Position = "Front"; x.Fitments = [Swift]; x.SaleType = "FixedPrice";
        }),
        Listing("bonnet-direct", x =>
        {
            x.Kind = "VehicleSpecificPart"; x.FitmentMode = "VehicleSpecific"; x.PartType = "Bonnet"; x.Category = "Body – Exterior";
            x.Title = "Toyota Hilux Bonnet"; x.Fitments = [Hilux]; x.SaleType = "FixedPrice";
        }),
        Listing("headlamp-auction", x =>
        {
            x.Kind = "VehicleSpecificPart"; x.FitmentMode = "VehicleSpecific"; x.PartType = "Headlamp"; x.Category = "Lighting";
            x.Title = "Maruti Suzuki Swift Left Headlamp"; x.Fitments = [Swift]; x.SaleType = "Auction";
        }),
        Listing("tyre-direct", x =>
        {
            x.Kind = "CommonPart"; x.FitmentMode = "SizeBased"; x.PartType = "Tyre"; x.Category = "Wheels & Tyres";
            x.Title = "Tyre 265/65 R17 – MRF – New"; x.Specs = new() { ["width"] = "265", ["profile"] = "65", ["rim"] = "17" };
            x.SaleType = "FixedPrice";
        }),
        Listing("hilux-vehicle", x =>
        {
            x.ItemType = "Vehicle"; x.Kind = "Vehicle"; x.Make = "Toyota"; x.Model = "Hilux"; x.Year = 2018;
            x.Title = "2018 Toyota Hilux"; x.Fitments = [new FitmentInfo { YearFrom = 2018, YearTo = 2018, Make = "Toyota", Model = "Hilux" }];
            x.SaleType = "Auction";
        }),
    ];

    private MeilisearchClient Client => fixture.Host.Services.GetRequiredService<MeilisearchClient>();

    public async Task InitializeAsync()
    {
        fixture.Host.Services.GetRequiredService<SearchDictionaryProvider>().Set(TestDictionary.Data);
        var task = await Client.Auctions().AddDocumentsAsync(_items, primaryKey: "id");
        await Client.WaitForTaskAsync(task.TaskUid);
    }

    public async Task DisposeAsync()
    {
        var task = await Client.Auctions().DeleteAllDocumentsAsync();
        await Client.WaitForTaskAsync(task.TaskUid);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<ListingSearchResponse> Search(string query)
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/search/listings?{Near}&pageSize=50&{query}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        return JsonSerializer.Deserialize<ListingSearchResponse>(await result.ReadAsTextAsync(), Json)!;
    }

    private static string[] Ids(ListingSearchResponse r) => r.Results.Select(x => x.Id).Order().ToArray();

    // Each filter alone and combined, with and without text: the exact listings, and the total is the result count.
    [Theory]
    [InlineData("partType=Bumper", new[] { "bumper-auction", "bumper-direct" })]
    [InlineData("q=bumper", new[] { "bumper-auction", "bumper-direct" })]
    [InlineData("category=Lighting", new[] { "headlamp-auction" })]
    [InlineData("category=Body%20%E2%80%93%20Exterior", new[] { "bonnet-direct", "bumper-auction", "bumper-direct" })]
    [InlineData("itemType=Part&make=Toyota&model=Hilux", new[] { "bonnet-direct", "bumper-auction" })]
    [InlineData("itemType=Vehicle", new[] { "hilux-vehicle" })]
    [InlineData("itemType=Vehicle&make=Toyota", new[] { "hilux-vehicle" })]
    [InlineData("itemType=Part&saleType=FixedPrice", new[] { "bonnet-direct", "bumper-direct", "tyre-direct" })]
    [InlineData("itemType=Part&saleType=Auction", new[] { "bumper-auction", "headlamp-auction" })]
    [InlineData("itemType=Part&saleType=FixedPrice&partType=Bumper", new[] { "bumper-direct" })]
    [InlineData("itemType=Part&saleType=FixedPrice&category=Body%20%E2%80%93%20Exterior&partType=Bumper", new[] { "bumper-direct" })]
    [InlineData("itemType=Part&category=Lighting&partType=Bumper", new string[0])]
    [InlineData("itemType=Part&q=front&partType=Bumper&make=Toyota&model=Hilux&year=2017", new[] { "bumper-auction" })]
    [InlineData("itemType=Part&q=swift&saleType=Auction", new[] { "headlamp-auction" })]
    public async Task Filters_OnlyMatchingListings_TotalIsTheResults(string query, string[] expected)
    {
        var response = await Search(query);

        Assert.Equal(expected.Order().ToArray(), Ids(response));
        Assert.Equal(expected.Length, response.TotalCount);
    }

    [Fact]
    public async Task CategoryCounts_MatchTheResultsOfThatCategory()
    {
        // What Home's tiles show (Parts, Direct sale) …
        var tiles = await Search("itemType=Part&saleType=FixedPrice");
        foreach (var tile in tiles.Facets["category"])
        {
            // … is exactly what tapping the tile returns.
            var picked = await Search($"itemType=Part&saleType=FixedPrice&category={Uri.EscapeDataString(tile.Value)}");
            Assert.Equal(tile.Count, picked.TotalCount);
            Assert.All(picked.Results, r => Assert.Equal(tile.Value, r.Category));
        }
        Assert.Equal(tiles.TotalCount, tiles.Facets["category"].Sum(c => c.Count));
    }

    [Fact]
    public async Task VehicleCount_MatchesTheVehiclesShown()
    {
        var vehicles = await Search("itemType=Vehicle");
        Assert.Equal(vehicles.Results.Count, vehicles.TotalCount);
        Assert.Equal(vehicles.TotalCount, vehicles.Facets["make"].Sum(m => m.Count));
    }

    private async Task<List<Suggestion>> Suggest(string text, string? itemType = "Part")
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/search/suggest?q={Uri.EscapeDataString(text)}&country=IN" + (itemType is null ? "" : $"&itemType={itemType}"));
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        using var doc = JsonDocument.Parse(await result.ReadAsTextAsync());
        return JsonSerializer.Deserialize<List<Suggestion>>(doc.RootElement.GetProperty("suggestions").GetRawText(), Json)!;
    }

    [Fact]
    public async Task Suggest_Category_GivesACategoryChip_ThatFiltersLikeATile()
    {
        var suggestions = await Suggest("light");

        var category = Assert.Single(suggestions, s => s.Group == "categories");
        Assert.Equal("Lighting", category.Label);
        Assert.Equal("All parts in Lighting", category.Subtitle);
        var chip = Assert.Single(category.Chips);
        Assert.Equal(("category", "Lighting"), (chip.Type, chip.Value));

        // The chip is sent as category=…, the same request a tile makes.
        var response = await Search($"itemType=Part&category={Uri.EscapeDataString(chip.Value)}");
        Assert.Equal(["headlamp-auction"], Ids(response));
    }

    [Fact]
    public async Task Suggest_PartType_ChipFiltersToThatPartType()
    {
        var bumper = Assert.Single(await Suggest("bumper"), s => s.Group == "parts" && s.Label == "Bumper");
        var chip = Assert.Single(bumper.Chips);

        var response = await Search($"itemType=Part&partType={Uri.EscapeDataString(chip.Value)}");
        Assert.Equal(["bumper-auction", "bumper-direct"], Ids(response));
    }

    [Fact]
    public async Task Suggest_NoCategoriesWhileVehiclesIsChosen()
    {
        Assert.DoesNotContain(await Suggest("light", "Vehicle"), s => s.Group == "categories");
    }
}

/// <summary>Index settings are applied on every start, so filters work after a restart or a wiped index.</summary>
[Collection("search-service")]
public class IndexSettingsTests(AppFixture fixture)
{
    private MeilisearchClient Client => fixture.Host.Services.GetRequiredService<MeilisearchClient>();

    [Fact]
    public async Task Restart_ReappliesFilterableAttributes_AfterTheyWereLost()
    {
        // Settings lost (e.g. a new Meilisearch volume, a reset or an index rebuilt by hand).
        var reset = await Client.Auctions().ResetFilterableAttributesAsync();
        await Client.WaitForTaskAsync(reset.TaskUid);
        Assert.Empty(await Client.Auctions().GetFilterableAttributesAsync());

        // Start the service again, the way start-backend.sh does: the settings come back on startup.
        await using (var restarted = await AlbaHost.For<Program>(builder =>
                     {
                         builder.UseEnvironment("Test");
                         builder.UseSetting("Meilisearch:Url", fixture.MeilisearchUrl);
                         builder.UseSetting("Meilisearch:ApiKey", "masterkey");
                         builder.ConfigureServices(services =>
                         {
                             services.RunWolverineInSoloMode();
                             services.DisableAllExternalWolverineTransports();
                         });
                     }))
        {
            var filterable = DbInitializer.FilterableNames(await Client.Auctions().GetFilterableAttributesAsync());
            foreach (var name in new[] { "category", "partType", "makes", "models", "listingType", "saleType", "_geo", "fitKeys" })
                Assert.Contains(name, filterable);

            // And a filtered search through the restarted service works.
            await restarted.Scenario(s =>
            {
                s.Get.Url("/api/search/listings?itemType=Part&saleType=FixedPrice&category=Lighting&partType=Bumper&lat=17.385&lon=78.4867");
                s.StatusCodeShouldBe(HttpStatusCode.OK);
            });
        }
    }

    [Fact]
    public async Task ApplySettings_SetsEveryFilterableAttribute()
    {
        await DbInitializer.ApplySettings(Client);

        var filterable = DbInitializer.FilterableNames(await Client.Auctions().GetFilterableAttributesAsync());
        Assert.Empty(DbInitializer.FilterableAttributes.Where(a => !filterable.Contains(a)));
    }

    [Fact]
    public async Task ConfigureIndex_RetriesThenFailsLoudly_WhenMeilisearchIsDown()
    {
        var down = new MeilisearchClient("http://localhost:1", "masterkey");

        await Assert.ThrowsAnyAsync<Exception>(() => DbInitializer.ConfigureIndex(down, attempts: 2, delay: TimeSpan.FromMilliseconds(10)));
    }
}

/// <summary>
/// Switching databases (local ↔ cloud, start-backend.sh): the shared index becomes an exact copy of the database in use –
/// its listings added or updated, listings only in the other database removed (no "Not found" from Search results).
/// </summary>
[Collection("search-service")]
public class FullResyncTests(AppFixture fixture)
{
    private MeilisearchClient Client => fixture.Host.Services.GetRequiredService<MeilisearchClient>();

    [Fact]
    public async Task FullResync_AddsAndUpdatesTheDatabasesListings_RemovesTheOthers()
    {
        // Its own index: the other tests' listings stay untouched.
        var name = $"resync-{Guid.NewGuid():N}";
        var index = Client.Index(name);
        var before = await index.AddDocumentsAsync(
            [TestData.CreateItem(i => { i.Id = "a"; i.Title = "Old title"; }), TestData.CreateItem(i => i.Id = "only-in-other-db")], primaryKey: "id");
        await Client.WaitForTaskAsync(before.TaskUid);

        await DbInitializer.FullResync(Client,
            [TestData.CreateItem(i => { i.Id = "a"; i.Title = "New title"; }), TestData.CreateItem(i => i.Id = "b")], name);

        var docs = (await index.GetDocumentsAsync<Item>(new Meilisearch.QueryParameters.DocumentsQuery { Limit = 100 })).Results.ToList();
        Assert.Equal(["a", "b"], docs.Select(d => d.Id).Order());
        Assert.Equal("New title", docs.Single(d => d.Id == "a").Title);
        await Client.DeleteIndexAsync(name);
    }
}
