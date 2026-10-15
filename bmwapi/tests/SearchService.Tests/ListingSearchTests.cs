using System.Net;
using System.Text.Json;
using Contracts;
using Meilisearch;
using Microsoft.Extensions.DependencyInjection;
using SearchService.Data;
using SearchService.Endpoints;
using SearchService.Models;
using SearchService.Services;
using Wolverine.Tracking;

namespace SearchService.Tests;

[Collection("search-service")]
public class ListingSearchTests(AppFixture fixture) : IAsyncLifetime
{
    // Hyderabad
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
        x.StartingPrice = 1000;
        x.CurrentHighBid = null;
        configure(x);
    }).Prepare();

    private readonly Item _hiluxBumper = Listing("hilux-bumper", x =>
    {
        x.Kind = "VehicleSpecificPart"; x.FitmentMode = "VehicleSpecific"; x.PartType = "Bumper"; x.Category = "Body – Exterior";
        x.Title = "2016–2020 Toyota Hilux Front Bumper – Genuine OEM – Used (Grade A)"; x.Position = "Front";
        x.Fitments = [new FitmentInfo { YearFrom = 2016, YearTo = 2020, Make = "Toyota", Model = "Hilux" }];
        x.PartNumbers = ["52119-0K935"]; x.Condition = "Used"; x.BrandType = "GenuineOem"; x.StartingPrice = 14000;
    });

    private readonly Item _rearHiluxBumper = Listing("hilux-rear-bumper", x =>
    {
        x.Kind = "VehicleSpecificPart"; x.FitmentMode = "VehicleSpecific"; x.PartType = "Bumper"; x.Category = "Body – Exterior";
        x.Title = "2016–2020 Toyota Hilux Rear Bumper"; x.Position = "Rear";
        x.Fitments = [new FitmentInfo { YearFrom = 2016, YearTo = 2020, Make = "Toyota", Model = "Hilux" }];
    });

    private readonly Item _farHiluxBumper = Listing("hilux-bumper-lagos", x =>
    {
        x.Kind = "VehicleSpecificPart"; x.FitmentMode = "VehicleSpecific"; x.PartType = "Bumper"; x.Position = "Front";
        x.Fitments = [new FitmentInfo { YearFrom = 2016, YearTo = 2020, Make = "Toyota", Model = "Hilux" }];
        x.Latitude = 6.5244; x.Longitude = 3.3792; x.CountryCode = "NG";
    });

    private readonly Item _corollaBumper = Listing("corolla-bumper", x =>
    {
        x.Kind = "VehicleSpecificPart"; x.FitmentMode = "VehicleSpecific"; x.PartType = "Bumper"; x.Position = "Front";
        x.Fitments = [new FitmentInfo { YearFrom = 2014, YearTo = 2019, Make = "Toyota", Model = "Corolla" }];
    });

    private readonly Item _swiftBonnet = Listing("swift-bonnet", x =>
    {
        x.Kind = "VehicleSpecificPart"; x.FitmentMode = "VehicleSpecific"; x.PartType = "Bonnet"; x.Category = "Body – Exterior";
        x.Title = "2018–2023 Maruti Suzuki Swift Bonnet";
        x.Fitments = [new FitmentInfo { YearFrom = 2018, YearTo = 2023, Make = "Maruti Suzuki", Model = "Swift" }];
        x.StartingPrice = 6500;
    });

    private readonly Item _tyre = Listing("tyre-265", x =>
    {
        x.Kind = "CommonPart"; x.FitmentMode = "SizeBased"; x.PartType = "Tyre"; x.Category = "Wheels & Tyres";
        x.Title = "Tyre 265/65 R17 – MRF – New"; x.Specs = new() { ["width"] = "265", ["profile"] = "65", ["rim"] = "17" };
        x.StartingPrice = 9800;
    });

    private readonly Item _dashCam = Listing("dash-cam", x =>
    {
        x.Kind = "CommonPart"; x.FitmentMode = "Universal"; x.PartType = "Dash cam"; x.Category = "Accessories & Tools";
        x.Title = "Dash Cam – 70mai – New"; x.StartingPrice = 3500;
    });

    private MeilisearchClient Client => fixture.Host.Services.GetRequiredService<MeilisearchClient>();

    public async Task InitializeAsync()
    {
        fixture.Host.Services.GetRequiredService<SearchDictionaryProvider>().Set(TestDictionary.Data);
        var task = await Client.Auctions().AddDocumentsAsync(
            [_hiluxBumper, _rearHiluxBumper, _farHiluxBumper, _corollaBumper, _swiftBonnet, _tyre, _dashCam], primaryKey: "id");
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
            s.Get.Url($"/api/search/listings?{query}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        return JsonSerializer.Deserialize<ListingSearchResponse>(await result.ReadAsTextAsync(), Json)!;
    }

    private static string Q(string text) => "q=" + Uri.EscapeDataString(text);

    [Fact]
    public async Task VehicleAndPartType_OnlyFittingPartsNearby_WithBadge()
    {
        var response = await Search($"{Q("2016 hilux front bumper")}&{Near}");

        var hit = Assert.Single(response.Results); // not the rear bumper, the Corolla one or the one in Lagos
        Assert.Equal("hilux-bumper", hit.Id);
        Assert.Equal("Fits your 2016 Toyota Hilux", hit.Badge);
        Assert.Equal(1, hit.MatchTier);
        Assert.NotNull(hit.DistanceKm);
        Assert.Equal(["partType", "make", "model", "year"], response.Chips.Select(c => c.Type));
    }

    [Fact]
    public async Task VehicleWithoutYear_ShowsCheckFitment()
    {
        var response = await Search($"{Q("bonnet swift")}&{Near}");

        var hit = Assert.Single(response.Results);
        Assert.Equal("swift-bonnet", hit.Id);
        Assert.Equal("Check fitment", hit.Badge);
    }

    [Fact]
    public async Task PartNumber_IsAnExactMatch_FirstTier()
    {
        var response = await Search($"{Q("52119 0k935")}&{Near}");

        var hit = Assert.Single(response.Results);
        Assert.Equal("hilux-bumper", hit.Id);
        Assert.Equal(0, hit.MatchTier);
    }

    [Fact]
    public async Task TyreSize_FindsSizeBasedTyres()
    {
        var response = await Search($"{Q("265 65 17")}&{Near}");

        Assert.Equal("tyre-265", Assert.Single(response.Results).Id);
        Assert.Equal("Fits by size", response.Results[0].Badge);
    }

    [Fact]
    public async Task UniversalParts_AreFoundByType()
    {
        var response = await Search($"{Q("dashcam")}&{Near}");

        Assert.Equal("Universal", Assert.Single(response.Results).Badge);
    }

    [Fact]
    public async Task NothingMatching_ReturnsNothing_NoSilentRelaxing()
    {
        var response = await Search($"{Q("2016 hilux bonnet")}&{Near}");

        Assert.Empty(response.Results);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task Chips_FilterSheetAndSort()
    {
        var bumpers = await Search($"partType=Bumper&make=Toyota&{Near}&sort=priceAsc");
        Assert.Equal(3, bumpers.TotalCount);
        Assert.Equal("hilux-bumper", bumpers.Results.Last().Id); // the most expensive last

        var common = await Search($"kind=CommonPart&{Near}");
        Assert.Equal(["dash-cam", "tyre-265"], common.Results.Select(r => r.Id).Order());

        var cheap = await Search($"maxPrice=5000&{Near}");
        Assert.All(cheap.Results, r => Assert.True(r.CurrentPrice <= 5000));

        var all = await Search(Near);
        Assert.Contains(all.Facets["partType"], f => f is { Value: "Bumper", Count: 3 });
        Assert.Contains(all.Facets["kind"], f => f.Value == "CommonPart");
        Assert.DoesNotContain(all.Results, r => r.Id == "hilux-bumper-lagos");
    }

    [Fact]
    public async Task CountryWide_WhenTheLocationIsOff()
    {
        var india = await Search("country=IN");
        var nigeria = await Search("country=NG");

        Assert.DoesNotContain(india.Results, r => r.Id == "hilux-bumper-lagos");
        Assert.Equal("hilux-bumper-lagos", Assert.Single(nigeria.Results).Id);
    }

    private async Task<List<Suggestion>> Suggest(string text, string? itemType = null)
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/search/suggest?q={Uri.EscapeDataString(text)}" + (itemType is null ? "" : $"&itemType={itemType}"));
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        using var doc = JsonDocument.Parse(await result.ReadAsTextAsync());
        return JsonSerializer.Deserialize<List<Suggestion>>(doc.RootElement.GetProperty("suggestions").GetRawText(), Json)!;
    }

    [Fact]
    public async Task Suggestions_GroupedLikeAShop()
    {
        var parts = await Suggest("front bump");
        Assert.Contains(parts, s => s.Group == "parts" && s.Label == "Bumper");

        var vehicle = await Suggest("2016 hilux front bumper");
        Assert.Contains(vehicle, s => s.Group == "parts" && s.Label == "Front bumper – 2016 Toyota Hilux");

        var vehicles = await Suggest("hilux");
        Assert.Contains(vehicles, s => s.Group == "vehicles" && s.Label == "Toyota Hilux 2016–2020");

        var numbers = await Suggest("52119");
        var number = Assert.Single(numbers, s => s.Group == "partNumbers");
        Assert.Equal("52119-0K935", number.Label);
        Assert.Equal("521190K935", Assert.Single(number.Chips).Value);
    }

    // ── Home filters (Parts / Vehicles, Auction / Direct sale) + search text together ──

    private async Task AddAsync(params Item[] items)
    {
        var task = await Client.Auctions().AddDocumentsAsync(items, primaryKey: "id");
        await Client.WaitForTaskAsync(task.TaskUid);
    }

    private static Item Vehicle(string id, string make, string model, int year, Action<Item>? configure = null) => Listing(id, x =>
    {
        x.Kind = "Vehicle"; x.ItemType = "Vehicle"; x.Make = make; x.Model = model; x.Year = year;
        x.Title = $"{year} {make} {model}"; x.StartingPrice = 900000;
        configure?.Invoke(x);
    });

    private static Item DirectSalePads(string id = "brake-pads-direct") => Listing(id, x =>
    {
        x.Kind = "CommonPart"; x.FitmentMode = "Universal"; x.PartType = "Brake pads"; x.Category = "Brakes";
        x.Title = "Brake pads – Bosch – New"; x.SaleType = "FixedPrice"; x.QuantityAvailable = 4; x.StartingPrice = 2500;
    });

    [Fact]
    public async Task PartsOrVehicles_AndSaleType_ApplyToResultsTotalsAndCategoryCounts()
    {
        await AddAsync(DirectSalePads(), Vehicle("hilux-2018", "Toyota", "Hilux", 2018), Vehicle("city-2019", "Honda", "City", 2019),
            Vehicle("city-2020", "Honda", "City", 2020, x => x.SaleType = "FixedPrice"));

        // Parts: category tiles with counts; no vehicles.
        var parts = await Search($"itemType=Part&{Near}");
        Assert.DoesNotContain(parts.Results, r => r.Kind == "Vehicle");
        Assert.Equal(parts.Results.Count, parts.TotalCount);
        Assert.Contains(parts.Facets["category"], f => f is { Value: "Body – Exterior", Count: 3 });
        Assert.Contains(parts.Facets["category"], f => f is { Value: "Brakes", Count: 1 });

        // Parts + Direct sale: only the brake pads, and only Brakes is counted (categories with 0 are left out).
        var direct = await Search($"itemType=Part&saleType=FixedPrice&{Near}");
        Assert.Equal(["brake-pads-direct"], direct.Results.Select(r => r.Id));
        Assert.Equal([new FacetValue("Brakes", 1)], direct.Facets["category"]);

        // Vehicles: the total and the make chips with counts.
        var vehicles = await Search($"itemType=Vehicle&{Near}");
        Assert.Equal(3, vehicles.TotalCount);
        Assert.Equal([new FacetValue("Honda", 2), new FacetValue("Toyota", 1)], vehicles.Facets["make"]);

        // Vehicles + Auction: the direct-sale City drops out of the list and the counts.
        var auctions = await Search($"itemType=Vehicle&saleType=Auction&{Near}");
        Assert.Equal(2, auctions.TotalCount);
        Assert.Contains(auctions.Facets["make"], f => f is { Value: "Honda", Count: 1 });
    }

    [Fact]
    public async Task SearchText_IsCombinedWithEveryFilter_AndCountsFollowTheSearch()
    {
        await AddAsync(DirectSalePads(), Vehicle("hilux-2018", "Toyota", "Hilux", 2018));

        // Text + Parts + category + radius: only bumpers near Hyderabad in that category.
        var bumpers = await Search($"{Q("bumper")}&itemType=Part&category={Uri.EscapeDataString("Body – Exterior")}&{Near}");
        // (The Corolla bumper has no category, the Lagos one is too far.)
        Assert.Equal(["hilux-bumper", "hilux-rear-bumper"], bumpers.Results.Select(r => r.Id).Order());
        Assert.Equal(2, bumpers.TotalCount);
        // The category chosen keeps its siblings (counted with the text): Brakes has no bumpers → not shown.
        Assert.Equal([new FacetValue("Body – Exterior", 2)], bumpers.Facets["category"]);

        // Text + Direct sale: no bumper is a direct sale.
        var none = await Search($"{Q("bumper")}&itemType=Part&saleType=FixedPrice&{Near}");
        Assert.Equal(0, none.TotalCount);
        Assert.All(none.Facets.Values, Assert.Empty);

        // Part type chip + category: part types within the category, with counts.
        var typed = await Search($"partType=Bumper&category={Uri.EscapeDataString("Body – Exterior")}&{Near}");
        Assert.Contains(typed.Facets["partType"], f => f is { Value: "Bumper", Count: 2 });
        Assert.Contains(typed.Facets["partType"], f => f is { Value: "Bonnet", Count: 1 });

        // Vehicles mode ignores parts that mention the text.
        var hilux = await Search($"{Q("hilux")}&itemType=Vehicle&{Near}");
        Assert.Equal(["hilux-2018"], hilux.Results.Select(r => r.Id));
    }

    [Fact]
    public async Task Radius_IsExact_ForResultsAndCounts()
    {
        // ~69 km from the search point: inside the old bounding box's corner, outside the 60 km circle.
        await AddAsync(Listing("bumper-69km", x =>
        {
            x.Kind = "VehicleSpecificPart"; x.FitmentMode = "VehicleSpecific"; x.PartType = "Bumper"; x.Category = "Body – Exterior";
            x.Latitude = 17.385 + 0.45; x.Longitude = 78.4867 + 0.45;
            x.Fitments = [new FitmentInfo { YearFrom = 2016, YearTo = 2020, Make = "Toyota", Model = "Hilux" }];
        }));

        var near = await Search($"partType=Bumper&{Near}");
        Assert.DoesNotContain(near.Results, r => r.Id == "bumper-69km");
        Assert.Contains(near.Facets["partType"], f => f is { Value: "Bumper", Count: 3 });

        var wider = await Search("partType=Bumper&lat=17.385&lon=78.4867&radiusKm=80");
        Assert.Contains(wider.Results, r => r.Id == "bumper-69km");
    }

    [Fact]
    public async Task Suggestions_FollowThePartsOrVehiclesChoice()
    {
        await AddAsync(Vehicle("hilux-2018", "Toyota", "Hilux", 2018));

        var vehicles = await Suggest("hilux", "Vehicle");
        Assert.All(vehicles, s => Assert.Equal("vehicles", s.Group));
        Assert.Contains(vehicles, s => s.Label == "Toyota Hilux 2018");

        var parts = await Suggest("hilux", "Part");
        Assert.DoesNotContain(parts, s => s.Group == "vehicles");
        Assert.Contains(parts, s => s.Group == "fits" && s.Label == "Parts for Toyota Hilux 2016–2020");

        // Part numbers and part types are not suggested while Vehicles is chosen.
        Assert.Empty(await Suggest("52119", "Vehicle"));
        Assert.DoesNotContain(await Suggest("front bump", "Vehicle"), s => s.Group == "parts");
    }

    [Fact]
    public async Task SellerRating_UpdatesTheSellersListings_Filter4StarsAndUp_SortTopRated()
    {
        var other = Listing("dash-cam-ravi", x =>
        {
            x.Kind = "CommonPart"; x.FitmentMode = "Universal"; x.PartType = "Dash cam"; x.Title = "Dash Cam – Ravi"; x.Seller = "ravi";
        });
        var add = await Client.Auctions().AddDocumentsAsync([other], primaryKey: "id");
        await Client.WaitForTaskAsync(add.TaskUid);

        await fixture.Host.InvokeMessageAndWaitAsync(new SellerRatingChanged { Seller = "seller", Average = 3.9, Count = 8 });
        await fixture.Host.InvokeMessageAndWaitAsync(new SellerRatingChanged { Seller = "ravi", Average = 4.8, Count = 21 });

        var updated = await Client.Auctions().GetDocumentAsync<Item>("dash-cam");
        Assert.Equal((3.9, 8), (updated.SellerRating, updated.SellerRatingCount));

        // "Seller rating 4★ & up": only ravi's.
        var top = await Search($"minSellerRating=4&{Near}");
        Assert.Equal(["dash-cam-ravi"], top.Results.Select(r => r.Id));
        Assert.Equal((4.8, 21), (top.Results[0].SellerRating, top.Results[0].SellerRatingCount));

        // "Top-rated sellers" first.
        var sorted = await Search($"{Q("dash cam")}&sort=topRated&{Near}");
        Assert.Equal("dash-cam-ravi", sorted.Results[0].Id);
    }
}
