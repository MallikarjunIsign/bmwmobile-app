using System.Net.Http.Json;
using System.Text.Json;
using SearchService.Endpoints;

namespace SearchService.Tests;

/// <summary>Runs only when GATEWAY_URL is set (pre-demo-check.sh sets it); skipped in normal test runs.</summary>
public sealed class LiveGatewayFactAttribute : FactAttribute
{
    public LiveGatewayFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GATEWAY_URL")))
            Skip = "Live test: set GATEWAY_URL (e.g. http://localhost:6001) with the backend running";
    }
}

public sealed class LiveGatewayTheoryAttribute : TheoryAttribute
{
    public LiveGatewayTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GATEWAY_URL")))
            Skip = "Live test: set GATEWAY_URL (e.g. http://localhost:6001) with the backend running";
    }
}

/// <summary>
/// The real path the app uses: app → Gateway (/search/...) → SearchService → Meilisearch, on the running backend
/// with its demo data around Hyderabad. Every filter the app sends must be applied, and the counts must be the results.
/// </summary>
[Trait("Category", "Live")]
public class GatewaySearchLiveTests
{
    private const string Near = "lat=17.385&lon=78.4867&radiusKm=60";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri((Environment.GetEnvironmentVariable("GATEWAY_URL") ?? "http://localhost:6001").TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(30),
    };

    private static async Task<ListingSearchResponse> Search(string query) =>
        (await Http.GetFromJsonAsync<ListingSearchResponse>($"search/listings?{Near}&pageSize=50&{query}", Json))!;

    private static async Task<List<Suggestion>> Suggest(string text, string itemType = "Part")
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync($"search/suggest?q={Uri.EscapeDataString(text)}&country=IN&itemType={itemType}"));
        return JsonSerializer.Deserialize<List<Suggestion>>(doc.RootElement.GetProperty("suggestions").GetRawText(), Json)!;
    }

    private static void TotalIsTheResults(ListingSearchResponse r) =>
        Assert.Equal(Math.Min(r.TotalCount, 50), r.Results.Count);

    [LiveGatewayFact]
    public async Task DemoData_IsIndexed_WithTheFieldsTheFiltersUse()
    {
        var parts = await Search("itemType=Part");
        Assert.True(parts.TotalCount > 0, "No parts near Hyderabad - is the demo data indexed?");
        Assert.All(parts.Results, r =>
        {
            Assert.False(string.IsNullOrEmpty(r.Category), $"{r.Id} has no category");
            Assert.False(string.IsNullOrEmpty(r.PartType), $"{r.Id} has no part type");
            Assert.False(string.IsNullOrEmpty(r.Kind), $"{r.Id} has no kind");
            Assert.Contains(r.SaleType, new[] { "Auction", "FixedPrice" });
        });
    }

    [LiveGatewayFact]
    public async Task PartTypeFilter_AloneAndWithText_OnlyThatPartType()
    {
        foreach (var query in new[] { "itemType=Part&partType=Bumper", "itemType=Part&q=bumper", "itemType=Part&q=front&partType=Bumper" })
        {
            var r = await Search(query);
            Assert.True(r.TotalCount > 0, $"{query}: no bumpers in the demo data");
            Assert.All(r.Results, x => Assert.Equal("Bumper", x.PartType));
            TotalIsTheResults(r);
        }
    }

    [LiveGatewayFact]
    public async Task EveryCategoryTile_ReturnsExactlyItsCount_AloneAndWithSaleType()
    {
        foreach (var sale in new[] { "", "&saleType=Auction", "&saleType=FixedPrice" })
        {
            var tiles = await Search("itemType=Part" + sale);
            Assert.Equal(tiles.TotalCount, tiles.Facets["category"].Sum(c => c.Count));
            foreach (var tile in tiles.Facets["category"])
            {
                var r = await Search($"itemType=Part{sale}&category={Uri.EscapeDataString(tile.Value)}");
                Assert.Equal(tile.Count, r.TotalCount);
                Assert.All(r.Results, x => Assert.Equal(tile.Value, x.Category));
                if (sale != "") Assert.All(r.Results, x => Assert.Equal(sale.Split('=')[1], x.SaleType));
                TotalIsTheResults(r);
            }
        }
    }

    [LiveGatewayFact]
    public async Task VehiclesAndMakes_CountsMatchTheResults()
    {
        var vehicles = await Search("itemType=Vehicle");
        Assert.All(vehicles.Results, x => Assert.Equal("Vehicle", x.Kind));
        Assert.Equal(vehicles.TotalCount, vehicles.Facets["make"].Sum(m => m.Count));
        foreach (var make in vehicles.Facets["make"])
        {
            var r = await Search($"itemType=Vehicle&make={Uri.EscapeDataString(make.Value)}");
            Assert.Equal(make.Count, r.TotalCount);
        }
    }

    /// <summary>
    /// Who sees what, on the demo data: pickup-only listings inside the radius, listings that ship from anywhere in the
    /// country, nothing from another country; the delivery counts are the results.
    /// </summary>
    [LiveGatewayTheory]
    [InlineData("lat=29.7604&lon=-95.3698&country=US&radiusKm=80.47", "US", "demo-us-la-alternator", "demo-us-phoenix-starter")]
    [InlineData("lat=17.385&lon=78.4867&country=IN&radiusKm=50", "IN", "demo-in-bengaluru-alternator", "demo-in-mumbai-gearbox")]
    public async Task DemoData_FarShippingListingShown_FarPickupOnlyHidden_NothingAbroad(string where, string country, string farShipping, string farPickup)
    {
        var r = (await Http.GetFromJsonAsync<ListingSearchResponse>($"search/listings?{where}&pageSize=50&itemType=Part", Json))!;

        Assert.Contains(r.Results, x => x.Id == farShipping && x.WithinRadius == false);
        Assert.DoesNotContain(r.Results, x => x.Id == farPickup);
        Assert.All(r.Results, x => Assert.Equal(country, x.Country));
        Assert.All(r.Results, x => Assert.True(x.OffersShipping || x.WithinRadius == true, $"{x.Id}: pickup only and outside the radius"));
        TotalIsTheResults(r);

        foreach (var option in r.Facets["delivery"])
        {
            var picked = (await Http.GetFromJsonAsync<ListingSearchResponse>($"search/listings?{where}&pageSize=50&itemType=Part&delivery={option.Value}", Json))!;
            Assert.Equal(option.Count, picked.TotalCount);
        }
    }

    [LiveGatewayFact]
    public async Task Suggestions_TheirChipsFilterTheResults()
    {
        var bumper = Assert.Single(await Suggest("bumper"), s => s.Label == "Bumper");
        var r = await Search($"itemType=Part&partType={Uri.EscapeDataString(bumper.Chips.Single(c => c.Type == "partType").Value)}");
        Assert.All(r.Results, x => Assert.Equal("Bumper", x.PartType));

        var lighting = Assert.Single(await Suggest("light"), s => s.Group == "categories");
        var category = lighting.Chips.Single(c => c.Type == "category").Value;
        r = await Search($"itemType=Part&category={Uri.EscapeDataString(category)}");
        Assert.All(r.Results, x => Assert.Equal(category, x.Category));

        Assert.Contains(await Suggest("brake"), s => s.Group == "categories" && s.Label == "Brakes");
        Assert.Contains(await Suggest("hilux"), s => s.Chips.Any(c => c.Type == "model" && c.Value == "Hilux"));
    }
}
