using System.Net;
using System.Text.Json;
using Alba;
using Contracts;
using Meilisearch;
using Microsoft.Extensions.DependencyInjection;
using SearchService.Data;
using SearchService.Endpoints;
using SearchService.Models;
using SearchService.Services;

namespace SearchService.Tests;

/// <summary>
/// Which listings a buyer sees (Contracts.ListingVisibility), with listings where the demo data has them:
/// pickup only → inside the buyer's radius; shipping only / pickup + shipping → anywhere in the buyer's country;
/// never another country. The same rule for the results, every count and the suggestions.
/// </summary>
[Collection("search-service")]
public class ListingVisibilityTests(AppFixture fixture) : IAsyncLifetime
{
    // Hyderabad, radius 50 km.
    private const string Hyderabad = "lat=17.385&lon=78.4867&country=IN";
    // Houston, radius 50 miles (the app sends km).
    private const string Houston = "lat=29.7604&lon=-95.3698&country=US";
    private static readonly double Miles50 = Math.Round(50 * RegionRules.KmPerMile, 2);
    private static readonly double Miles200 = Math.Round(200 * RegionRules.KmPerMile, 2);

    private static readonly string[] Pickup = ["Pickup"], Shipping = ["Shipping"], Both = ["Pickup", "Shipping"];

    private static Item Listing(string id, string country, double lat, double lon, string[] delivery, string category = "Engine",
        string? partNumber = null) =>
        TestData.CreateItem(x =>
        {
            x.Id = id;
            x.ItemType = "Part";
            x.Kind = "CommonPart";
            x.FitmentMode = "Universal";
            x.PartType = "Alternator";
            x.Category = category;
            x.Title = $"Alternator {id}";
            x.Make = "";
            x.Model = "";
            x.Year = 0;
            x.CountryCode = country;
            x.Latitude = lat;
            x.Longitude = lon;
            x.DeliveryOptions = delivery.ToList();
            x.PartNumbers = partNumber is null ? [] : [partNumber];
            x.SaleType = "FixedPrice";
        }).Prepare();

    private readonly Item[] _items =
    [
        // India (buyer in Hyderabad)
        Listing("in-pickup-kukatpally", "IN", 17.4849, 78.4138, Pickup),                    // ~13 km
        Listing("in-both-madhapur", "IN", 17.4483, 78.3915, Both, "Lighting"),              // ~12 km
        Listing("in-pickup-warangal", "IN", 18.0072, 79.5584, Pickup, partNumber: "RAD-77001"), // ~135 km
        Listing("in-pickup-mumbai", "IN", 19.1136, 72.8697, Pickup),                        // ~620 km
        Listing("in-ship-bengaluru", "IN", 12.9698, 77.7500, Shipping, partNumber: "ALT-77002"), // ~500 km
        Listing("in-both-delhi", "IN", 28.6139, 77.2090, Both, "Lighting"),                 // ~1,250 km
        // USA (buyer in Houston)
        Listing("us-pickup-pasadena", "US", 29.6911, -95.2091, Pickup),                     // ~11 mi
        Listing("us-pickup-roundrock", "US", 30.5083, -97.6789, Pickup),                    // ~150 mi
        Listing("us-pickup-tempe", "US", 33.4255, -111.9400, Pickup),                       // ~1,000 mi
        Listing("us-ship-longbeach", "US", 33.7701, -118.1937, Shipping, partNumber: "ALT-77003"), // ~1,370 mi
        Listing("us-both-dallas", "US", 32.7767, -96.7970, Both, "Lighting"),               // ~225 mi
        // Nigeria, Ireland: never shown to these buyers.
        Listing("ng-ship-lagos", "NG", 6.6018, 3.3515, Shipping),
        Listing("ie-both-dublin", "IE", 53.3498, -6.2603, Both),
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

    private async Task<ListingSearchResponse> Search(string where, double radiusKm, string query = "")
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/search/listings?{where}&radiusKm={radiusKm.ToString(System.Globalization.CultureInfo.InvariantCulture)}&pageSize=50&itemType=Part&{query}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        return JsonSerializer.Deserialize<ListingSearchResponse>(await result.ReadAsTextAsync(), Json)!;
    }

    private static string[] Ids(ListingSearchResponse r) => r.Results.Select(x => x.Id).Order().ToArray();

    [Fact]
    public async Task HyderabadBuyer_Radius50_PickupOnlyNearby_ShippingFromAllOfIndia_NothingAbroad()
    {
        var r = await Search(Hyderabad, 50);

        Assert.Equal(["in-both-delhi", "in-both-madhapur", "in-pickup-kukatpally", "in-ship-bengaluru"], Ids(r));
        Assert.Equal(4, r.TotalCount);
        // Every result has its distance; only the ones inside the radius offer pickup to this buyer.
        Assert.All(r.Results, x => Assert.NotNull(x.DistanceKm));
        Assert.True(r.Results.Single(x => x.Id == "in-both-madhapur").WithinRadius);
        Assert.False(r.Results.Single(x => x.Id == "in-both-delhi").WithinRadius);
        Assert.False(r.Results.Single(x => x.Id == "in-ship-bengaluru").WithinRadius);
    }

    [Fact]
    public async Task HoustonBuyer_Radius50Miles_PickupOnlyNearby_ShippingFromAllOfTheUsa_NothingAbroad()
    {
        var r = await Search(Houston, Miles50);

        Assert.Equal(["us-both-dallas", "us-pickup-pasadena", "us-ship-longbeach"], Ids(r));
        Assert.Equal(3, r.TotalCount);
        Assert.True(r.Results.Single(x => x.Id == "us-pickup-pasadena").WithinRadius);
        Assert.False(r.Results.Single(x => x.Id == "us-ship-longbeach").WithinRadius);
    }

    [Fact]
    public async Task BuyerCountry_ComesFromTheCoordinates_WhenNotSent()
    {
        var r = await Search("lat=29.7604&lon=-95.3698", Miles50);
        Assert.Equal(["us-both-dallas", "us-pickup-pasadena", "us-ship-longbeach"], Ids(r));
    }

    [Theory]
    [InlineData(Hyderabad, 50, 200, "in-pickup-warangal")]
    [InlineData(Houston, 80.47, 321.87, "us-pickup-roundrock")]
    public async Task LargerRadius_AddsFarPickupOnlyItems_ShippingItemsStayTheSame(string where, double small, double large, string added)
    {
        var before = await Search(where, small);
        var after = await Search(where, large);

        Assert.Equal(Ids(before).Append(added).Order().ToArray(), Ids(after));
        Assert.Equal(
            before.Results.Where(x => x.OffersShipping).Select(x => x.Id).Order().ToArray(),
            after.Results.Where(x => x.OffersShipping).Select(x => x.Id).Order().ToArray());
    }

    [Fact]
    public async Task RadiusAboveTheOld500KmLimit_IsApplied()
    {
        // Mumbai (pickup only) is ~620 km from Hyderabad: shown with a 1,000 km radius.
        var r = await Search(Hyderabad, 1000);
        Assert.True(r.Results.Single(x => x.Id == "in-pickup-mumbai").WithinRadius);
        // Tempe is ~1,006 mi from Houston: just outside the largest radius (1,000 mi).
        Assert.DoesNotContain("us-pickup-tempe", Ids(await Search(Houston, RegionRules.MaxRadiusKm)));
    }

    [Fact]
    public async Task UsaRadius200Miles_StillHidesPickupOnlyFromArizona()
    {
        var r = await Search(Houston, Miles200);
        Assert.Contains("us-pickup-roundrock", Ids(r));
        Assert.DoesNotContain("us-pickup-tempe", Ids(r));
    }

    [Theory]
    [InlineData(Hyderabad, 50)]
    [InlineData(Houston, 80.47)]
    public async Task Counts_AreTheResults(string where, double radius)
    {
        var all = await Search(where, radius);

        // Category tiles.
        Assert.Equal(all.TotalCount, all.Facets["category"].Sum(c => c.Count));
        foreach (var tile in all.Facets["category"])
            Assert.Equal(tile.Count, (await Search(where, radius, $"category={Uri.EscapeDataString(tile.Value)}")).TotalCount);

        // Delivery filter: Pickup = pickup inside the radius, Shipping = everything that ships in the country.
        foreach (var option in all.Facets["delivery"])
        {
            var picked = await Search(where, radius, $"delivery={option.Value}");
            Assert.Equal(option.Count, picked.TotalCount);
            if (option.Value == "Pickup") Assert.All(picked.Results, x => Assert.True(x.OffersPickup && x.WithinRadius == true, x.Id));
            else Assert.All(picked.Results, x => Assert.True(x.OffersShipping, x.Id));
        }
    }

    [Fact]
    public async Task DeliveryFilter_Pickup_OnlyInsideTheRadius_Shipping_TheWholeCountry()
    {
        Assert.Equal(["in-both-madhapur", "in-pickup-kukatpally"], Ids(await Search(Hyderabad, 50, "delivery=Pickup")));
        Assert.Equal(["in-both-delhi", "in-both-madhapur", "in-ship-bengaluru"], Ids(await Search(Hyderabad, 50, "delivery=Shipping")));
        Assert.Equal(["us-pickup-pasadena"], Ids(await Search(Houston, Miles50, "delivery=Pickup")));
        Assert.Equal(["us-both-dallas", "us-ship-longbeach"], Ids(await Search(Houston, Miles50, "delivery=Shipping")));
    }

    [Fact]
    public async Task NearestFirst_SortsEveryResultByDistance()
    {
        var r = await Search(Houston, Miles50, "sort=nearest");
        Assert.Equal(["us-pickup-pasadena", "us-both-dallas", "us-ship-longbeach"], r.Results.Select(x => x.Id).ToArray());
    }

    [Fact]
    public async Task LocationOff_TheWholeCountry()
    {
        var r = await Search("country=US", 50);
        Assert.Equal(["us-both-dallas", "us-pickup-pasadena", "us-pickup-roundrock", "us-pickup-tempe", "us-ship-longbeach"], Ids(r));
    }

    [Fact]
    public async Task SellerProfile_FollowsTheSameRule()
    {
        // All test listings have the same seller: their profile shows a buyer only what the buyer can see.
        var r = await Search(Houston, Miles50, "seller=seller");
        Assert.Equal(["us-both-dallas", "us-pickup-pasadena", "us-ship-longbeach"], Ids(r));
    }

    private async Task<List<Suggestion>> Suggest(string text, string where, double radiusKm)
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/search/suggest?q={Uri.EscapeDataString(text)}&{where}&radiusKm={radiusKm.ToString(System.Globalization.CultureInfo.InvariantCulture)}&itemType=Part");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        using var doc = JsonDocument.Parse(await result.ReadAsTextAsync());
        return JsonSerializer.Deserialize<List<Suggestion>>(doc.RootElement.GetProperty("suggestions").GetRawText(), Json)!;
    }

    private static string[] PartNumbers(List<Suggestion> s) => s.Where(x => x.Group == "partNumbers").Select(x => x.Label).ToArray();

    [Fact]
    public async Task Suggestions_PartNumbers_FollowTheSameRule()
    {
        // Shipping-only in Bengaluru: suggested in Hyderabad.
        Assert.Equal(["ALT-77002"], PartNumbers(await Suggest("77002", Hyderabad, 50)));
        // Pickup-only in Warangal (~135 km): not at 50 km, yes at 200 km.
        Assert.Empty(PartNumbers(await Suggest("77001", Hyderabad, 50)));
        Assert.Equal(["RAD-77001"], PartNumbers(await Suggest("77001", Hyderabad, 200)));
        // A US listing is never suggested in India, and is in the USA.
        Assert.Empty(PartNumbers(await Suggest("77003", Hyderabad, 50)));
        Assert.Equal(["ALT-77003"], PartNumbers(await Suggest("77003", Houston, Miles50)));
    }
}

/// <summary>The rule itself (also used by the part-request auto-match).</summary>
public class ListingVisibilityRuleTests
{
    [Theory]
    // pickup only: radius
    [InlineData(17.4849, 78.4138, "IN", false, true)]
    [InlineData(18.0072, 79.5584, "IN", false, false)]
    // ships: anywhere in the country
    [InlineData(12.9698, 77.75, "IN", true, true)]
    [InlineData(28.6139, 77.209, "IN", true, true)]
    // never another country
    [InlineData(29.7604, -95.3698, "US", true, false)]
    [InlineData(6.6018, 3.3515, "NG", true, false)]
    public void HyderabadBuyer(double lat, double lon, string country, bool ships, bool visible) =>
        Assert.Equal(visible, ListingVisibility.IsVisible("IN", 17.385, 78.4867, 50, country, lat, lon, ships));

    [Fact]
    public void CountryOf_FromTheCodeOrTheCoordinates()
    {
        Assert.Equal("US", ListingVisibility.CountryOf("us", null, null));
        Assert.Equal("US", ListingVisibility.CountryOf(null, 29.76, -95.37));
        Assert.Equal("IN", ListingVisibility.CountryOf("", 17.385, 78.4867));
        Assert.Null(ListingVisibility.CountryOf(null, 48.85, 2.35));
    }
}
