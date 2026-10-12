using System.Net;
using Contracts;
using Meilisearch;
using Microsoft.Extensions.DependencyInjection;
using SearchService.Data;
using SearchService.Models;
using Wolverine.Tracking;

namespace SearchService.Tests;

[Collection("search-service")]
public class SearchFeatureTests(AppFixture fixture) : IAsyncLifetime
{
    private static ContainerInfo Container(string destinationPort, string destinationCountry) => new()
    {
        Id = $"c-{destinationPort}",
        ContainerNumber = "MSCU1234567",
        OriginPort = "Jebel Ali",
        OriginCountry = "United Arab Emirates",
        DestinationPort = destinationPort,
        DestinationCountry = destinationCountry,
        DepartureDate = DateTime.UtcNow.AddDays(-3),
        EstimatedArrivalDate = DateTime.UtcNow.AddDays(9),
        ShippingStatus = "InTransit"
    };

    private readonly Item _vehicle = TestData.CreateItem(x => x.Make = "Vehicleone");

    private readonly Item _bumper = TestData.CreateItem(x =>
    {
        x.ItemType = "Part";
        x.PartName = "Front bumper";
        x.PartCategory = "Body";
        x.Condition = "Used";
        x.Make = "Toyota";
        x.Model = "Hilux";
        x.Container = Container("Nhava Sheva", "India");
    });

    private readonly Item _wheels = TestData.CreateItem(x =>
    {
        x.ItemType = "Part";
        x.PartName = "Alloy wheels";
        x.PartCategory = "WheelsAndTyres";
        x.Condition = "New";
        x.Container = Container("Durban", "South Africa");
    });

    private readonly Item _sold = TestData.CreateItem(x =>
    {
        x.Make = "Soldcar";
        x.Status = "Sold";       // accepted early: end time still in the future
    });

    private readonly Item _waiting = TestData.CreateItem(x =>
    {
        x.Make = "Waitingcar";
        x.Status = "ReserveNotMet";
        x.AuctionEnd = DateTime.UtcNow.AddHours(-1);
    });

    private MeilisearchClient Client => fixture.Host.Services.GetRequiredService<MeilisearchClient>();

    public async Task InitializeAsync()
    {
        var task = await Client.Auctions().AddDocumentsAsync([_vehicle, _bumper, _wheels, _sold, _waiting], primaryKey: "id");
        await Client.WaitForTaskAsync(task.TaskUid);
    }

    public async Task DisposeAsync()
    {
        var task = await Client.Auctions().DeleteAllDocumentsAsync();
        await Client.WaitForTaskAsync(task.TaskUid);
    }

    private record SearchResponse(List<Item> Results, int PageCount, int TotalCount);

    private async Task<List<string>> Search(string query)
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/search?pageSize=50&{query}");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        return (await result.ReadAsJsonAsync<SearchResponse>()).Results.Select(x => x.Id).ToList();
    }

    private async Task<Item> Get(string id) => await Client.Auctions().GetDocumentAsync<Item>(id);

    [Theory]
    [InlineData("itemType=part", 2)]
    [InlineData("itemType=Parts", 2)]
    [InlineData("itemType=vehicle", 3)]
    [InlineData("itemType=all", 5)]
    public async Task Search_ByItemType(string query, int expected)
    {
        Assert.Equal(expected, (await Search(query)).Count);
    }

    [Fact]
    public async Task Search_ByPartCategoryAndCondition()
    {
        Assert.Equal([_wheels.Id], await Search("partCategory=WheelsAndTyres"));
        Assert.Equal([_bumper.Id], await Search("itemType=part&condition=Used"));
    }

    [Fact]
    public async Task Search_ByDestination_MatchesPortOrCountry()
    {
        Assert.Equal([_bumper.Id], await Search("destination=India"));
        Assert.Equal([_wheels.Id], await Search("destination=Durban"));
    }

    [Fact]
    public async Task Search_ByPartName()
    {
        Assert.Equal([_bumper.Id], await Search("searchTerm=bumper"));
    }

    [Fact]
    public async Task Search_ByMarketplacePills_CombinesWithEachOtherAndTheText()
    {
        Assert.Equal([_bumper.Id], await Search("partCategory=Body"));
        Assert.Equal([_bumper.Id], await Search("partCategory=Body&make=Toyota&model=Hilux"));
        Assert.Equal([_bumper.Id], await Search("make=Toyota&searchTerm=bumper"));
        Assert.Empty(await Search("partCategory=Body&model=Corolla"));
        Assert.Empty(await Search("make=Toyota&searchTerm=wheels"));
    }

    private record FilterOptionsResponse(
        List<FilterOption> Categories, List<FilterOption> Makes, List<ModelOption> Models, int TotalCount);
    private record FilterOption(string Value, int Count);
    private record ModelOption(string Make, string Model, int Count);

    [Fact]
    public async Task FilterOptions_ListTheLiveCategoriesAndModels()
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/search/filters");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        var options = await result.ReadAsJsonAsync<FilterOptionsResponse>();

        Assert.Equal(["Body", "WheelsAndTyres"], options.Categories.Select(x => x.Value).Order());
        Assert.Contains(options.Models, x => x is { Make: "Toyota", Model: "Hilux", Count: 1 });
        // Sold / waiting auctions don't produce pills.
        Assert.DoesNotContain(options.Makes, x => x.Value is "Soldcar" or "Waitingcar");
        Assert.Equal(3, options.TotalCount);
    }

    [Fact]
    public async Task Search_Live_ExcludesEndedAuctions()
    {
        var live = await Search("filterBy=live");

        Assert.DoesNotContain(_sold.Id, live);
        Assert.DoesNotContain(_waiting.Id, live);
        Assert.Contains(_vehicle.Id, live);
    }

    [Fact]
    public async Task Search_Finished_IncludesSoldAndReserveNotMet()
    {
        var finished = await Search("filterBy=finished");

        Assert.Equal(new[] { _sold.Id, _waiting.Id }.Order(), finished.Order());
    }

    [Fact]
    public async Task Search_FilterValueWithQuotes_DoesNotBreakTheQuery()
    {
        Assert.Empty(await Search("seller=bob%22%20OR%20seller%20%3D%20%22seller"));
    }

    [Fact]
    public async Task AuctionCreated_IndexesWithoutReservePrice()
    {
        var message = new AuctionCreated
        {
            Id = Guid.NewGuid().ToString(),
            Seller = "bob",
            Status = "Live",
            ItemType = "Part",
            Make = "Ford",
            Model = "Ranger",
            Description = "LED headlight",
            PartName = "LED headlight",
            StartingPrice = 250,
            ReservePrice = 400,
            BidIncrement = 20,
            AuctionEnd = DateTime.UtcNow.AddDays(1),
            ImageUrl = "http://localhost:6001/images/a.jpg",
            Images = ["http://localhost:6001/images/a.jpg"]
        };

        await fixture.Host.InvokeMessageAndWaitAsync(message);

        // AddDocuments is processed asynchronously by Meilisearch: wait until the document is there.
        var json = "";
        for (var attempt = 0; attempt < 50 && !json.Contains(message.Id); attempt++)
        {
            var result = await fixture.Host.Scenario(s =>
            {
                s.Get.Url($"/api/search/{message.Id}");
                s.IgnoreStatusCode();
            });
            json = await result.ReadAsTextAsync();
            if (!json.Contains(message.Id)) await Task.Delay(100);
        }

        Assert.DoesNotContain("reservePrice", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"partName\":\"LED headlight\"", json);
    }

    [Fact]
    public async Task BidPlaced_SetsHighBidAndReserveMet()
    {
        await fixture.Host.InvokeMessageAndWaitAsync(Bid(_bumper.Id, 150, "AcceptedBelowReserve"));
        Assert.False((await Get(_bumper.Id)).ReserveMet);

        await fixture.Host.InvokeMessageAndWaitAsync(Bid(_bumper.Id, 200, "Accepted"));
        var item = await Get(_bumper.Id);
        Assert.Equal(200, item.CurrentHighBid);
        Assert.True(item.ReserveMet);

        // an older, lower bid arriving late doesn't lower the high bid
        await fixture.Host.InvokeMessageAndWaitAsync(Bid(_bumper.Id, 175, "Accepted"));
        Assert.Equal(200, (await Get(_bumper.Id)).CurrentHighBid);
    }

    [Fact]
    public async Task AuctionFinished_ReserveNotMetThenSold()
    {
        await fixture.Host.InvokeMessageAndWaitAsync(Finished(_vehicle.Id, "ReserveNotMet", false));
        var waiting = await Get(_vehicle.Id);
        Assert.Equal("ReserveNotMet", waiting.Status);
        Assert.NotNull(waiting.SellerDecisionDeadline);

        await fixture.Host.InvokeMessageAndWaitAsync(Finished(_vehicle.Id, "Sold", true, "SellerAcceptedAfterEnd"));
        var sold = await Get(_vehicle.Id);
        Assert.Equal("Sold", sold.Status);
        Assert.Equal("alice", sold.Winner);
        Assert.Equal("SellerAcceptedAfterEnd", sold.EndReason);
    }

    [Fact]
    public async Task AuctionFinished_UnsoldAfterSold_IsIgnored()
    {
        await fixture.Host.InvokeMessageAndWaitAsync(Finished(_sold.Id, "Unsold", false, "GracePeriodExpired"));

        Assert.Equal("Sold", (await Get(_sold.Id)).Status);
    }

    [Fact]
    public async Task AuctionExtended_MovesEndLater()
    {
        var newEnd = _vehicle.AuctionEnd.AddMinutes(2);

        await fixture.Host.InvokeMessageAndWaitAsync(new AuctionExtended { AuctionId = _vehicle.Id, AuctionEnd = newEnd });

        Assert.Equal(newEnd, (await Get(_vehicle.Id)).AuctionEnd, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task ContainerUpdated_UpdatesLinkedAuctions()
    {
        var container = _bumper.Container!;
        var message = new ContainerUpdated
        {
            Id = container.Id,
            ContainerNumber = container.ContainerNumber,
            OriginPort = container.OriginPort,
            OriginCountry = container.OriginCountry,
            DestinationPort = container.DestinationPort,
            DestinationCountry = container.DestinationCountry,
            DepartureDate = container.DepartureDate,
            EstimatedArrivalDate = container.EstimatedArrivalDate.AddDays(5),
            ShippingStatus = "Arrived",
            AuctionIds = [_bumper.Id]
        };

        await fixture.Host.InvokeMessageAndWaitAsync(message);

        var item = await Get(_bumper.Id);
        Assert.Equal("Arrived", item.Container!.ShippingStatus);
        Assert.Equal(message.EstimatedArrivalDate, item.Container.EstimatedArrivalDate, TimeSpan.FromMilliseconds(1));
        Assert.Equal("Front bumper", item.PartName); // the rest of the document is untouched
    }

    private static BidPlaced Bid(string auctionId, int amount, string status) => new()
    {
        Id = Guid.NewGuid().ToString(),
        AuctionId = auctionId,
        Bidder = "alice",
        BidTime = DateTime.UtcNow,
        Amount = amount,
        BidStatus = status
    };

    private static AuctionFinished Finished(string auctionId, string status, bool sold, string reason = "TimeEnded") => new()
    {
        AuctionId = auctionId,
        Seller = "seller",
        ItemSold = sold,
        Winner = sold ? "alice" : null,
        Amount = 150,
        Status = status,
        EndReason = reason,
        SellerDecisionDeadline = status == "ReserveNotMet" ? DateTime.UtcNow.AddHours(24) : null
    };
}