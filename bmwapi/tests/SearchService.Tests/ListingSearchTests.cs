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

    private async Task<List<Suggestion>> Suggest(string text)
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/search/suggest?q={Uri.EscapeDataString(text)}");
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
