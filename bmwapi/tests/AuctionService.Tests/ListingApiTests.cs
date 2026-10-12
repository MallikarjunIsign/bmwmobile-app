using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using AuctionService.Controllers;
using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Tracking;

namespace AuctionService.Tests;

[Collection("auction-service")]
public class ListingApiTests(AppFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        db.Orders.RemoveRange(db.Orders);
        db.GarageVehicles.RemoveRange(db.GarageVehicles);
        db.ShippingAddresses.RemoveRange(db.ShippingAddresses);
        db.Auctions.RemoveRange(db.Auctions);
        db.Makes.RemoveRange(db.Makes.Where(m => !m.IsApproved));
        await db.SaveChangesAsync();
    }

    private async Task<int> PartTypeId(string name)
    {
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        return (await db.PartTypes.SingleAsync(p => p.Name == name)).Id;
    }

    private async Task<T> Send<T>(Action<Alba.Scenario> configure, string user = "bob", HttpStatusCode status = HttpStatusCode.OK)
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", user));
            s.WithClaim(new Claim("email", $"{user}@example.com"));
            configure(s);
            s.StatusCodeShouldBe(status);
        });
        return await result.ReadAsJsonAsync<T>();
    }

    private async Task<string> SendText(Action<Alba.Scenario> configure, HttpStatusCode status, string user = "bob")
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", user));
            configure(s);
            s.StatusCodeShouldBe(status);
        });
        return await result.ReadAsTextAsync();
    }

    private async Task<AuctionDto> CreateDraft(string kind, string? partType)
    {
        var dto = new CreateListingDto
        {
            Kind = kind, PartTypeId = partType is null ? null : await PartTypeId(partType), CountryCode = "IN",
            LocationName = "Kukatpally, Hyderabad", Latitude = 17.4849, Longitude = 78.4138
        };
        return await Send<AuctionDto>(s => s.Post.Json(dto).ToUrl("/api/listings"), status: HttpStatusCode.Created);
    }

    private Task<AuctionDto> Save(string id, SaveListingDto dto, string user = "bob") =>
        Send<AuctionDto>(s => s.Put.Json(dto).ToUrl($"/api/listings/{id}"), user);

    private async Task AddPhotos(string id, int count)
    {
        var form = new MultipartFormDataContent();
        for (var i = 0; i < count; i++)
        {
            var content = new ByteArrayContent(TestImages.Jpeg());
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(content, "files", $"{i}.jpg");
        }
        await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", "bob"));
            s.Post.MultipartFormData(form).ToUrl($"/api/auctions/{id}/images");
            s.StatusCodeShouldBe(HttpStatusCode.OK);
        });
    }

    private static SaveListingDto HiluxBumper() => new()
    {
        Fitments = [new FitmentDto { YearFrom = 2016, YearTo = 2020, Make = "Toyota", Model = "Hilux" }],
        PartNumbers = [new PartNumberDto { Value = "52119-0K935" }],
        PartDetails = new PartDetailsDto { BrandType = "GenuineOem", Condition = "Used", UsedGrade = "A", Position = "Front" },
        PartPrice = 14000,
        DurationDays = 3,
        DeliveryOptions = ["Pickup"],
        Description = "Front bumper, light scuffs"
    };

    private async Task<AuctionDto> PublishedHiluxBumper()
    {
        var draft = await CreateDraft("VehicleSpecificPart", "Bumper");
        await Save(draft.Id, HiluxBumper());
        await AddPhotos(draft.Id, 3);
        return await Send<AuctionDto>(s => s.Post.Json(new { }).ToUrl($"/api/listings/{draft.Id}/publish"));
    }

    [Fact]
    public async Task Draft_SavePhotosPublish_GoesLiveAndIsAnnounced()
    {
        var draft = await CreateDraft("VehicleSpecificPart", "Bumper");
        Assert.Equal("Draft", draft.Status);
        Assert.Equal("INR", draft.Currency);

        // Drafts are private: not in the public list (or the search sync).
        var list = await Send<List<AuctionDto>>(s => s.Get.Url("/api/auctions"));
        Assert.DoesNotContain(list, x => x.Id == draft.Id);

        var saved = await Save(draft.Id, HiluxBumper());
        Assert.Equal("2016–2020 Toyota Hilux Front Bumper – Genuine OEM – Used (Grade A)", saved.Title);
        Assert.True(saved.CanEdit);

        await AddPhotos(draft.Id, 3);

        AuctionDto? published = null;
        var tracked = await fixture.Host.ExecuteAndWaitAsync(async () =>
            published = await Send<AuctionDto>(s => s.Post.Json(new { }).ToUrl($"/api/listings/{draft.Id}/publish")));

        Assert.Equal("Live", published!.Status);
        Assert.Equal(14000, published.StartingPrice);
        Assert.Equal(DateTime.UtcNow.AddDays(3), published.AuctionEnd, TimeSpan.FromMinutes(1));
        var created = tracked.Sent.SingleMessage<AuctionCreated>();
        Assert.Equal("Bumper", created.PartType);
        Assert.Equal("VehicleSpecific", created.FitmentMode);
        Assert.Equal(["52119-0K935"], created.PartNumbers);
        Assert.Equal("Toyota", Assert.Single(created.Fitments).Make);
        Assert.Equal(14000, created.ReservePrice); // no reserve
    }

    [Fact]
    public async Task Publish_Incomplete_Returns400WithWhatIsMissing()
    {
        var draft = await CreateDraft("VehicleSpecificPart", "Bumper");

        var body = await SendText(s => s.Post.Json(new { }).ToUrl($"/api/listings/{draft.Id}/publish"), HttpStatusCode.BadRequest);

        Assert.Contains("fitments", body);
        Assert.Contains("photos", body);
        Assert.Contains("brandType", body);
    }

    [Fact]
    public async Task OtherUsers_CannotSeeOrChangeADraft()
    {
        var draft = await CreateDraft("CommonPart", "Dash cam");

        await SendText(s => s.Get.Url($"/api/listings/{draft.Id}"), HttpStatusCode.NotFound, "alice");
        await SendText(s => s.Put.Json(new SaveListingDto { PartPrice = 1 }).ToUrl($"/api/listings/{draft.Id}"), HttpStatusCode.Forbidden, "alice");
    }

    [Fact]
    public async Task AfterTheFirstBid_OnlyNotesAndPhotos()
    {
        var live = await PublishedHiluxBumper();
        using (var scope = fixture.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
            var a = await db.Auctions.FindAsync(live.Id);
            a!.CurrentHighBid = 14010;
            a.BidCount = 1;
            await db.SaveChangesAsync();
        }

        await SendText(s => s.Put.Json(new SaveListingDto { PartPrice = 1 }).ToUrl($"/api/listings/{live.Id}"), HttpStatusCode.BadRequest);
        await SendText(s => s.Post.Json(new { }).ToUrl($"/api/listings/{live.Id}/cancel"), HttpStatusCode.BadRequest);

        var noted = await Send<AuctionDto>(s => s.Post.Json(new AddNoteDto { Text = "Also includes the clips" }).ToUrl($"/api/listings/{live.Id}/notes"));
        Assert.Contains("Also includes the clips", noted.Description);
        await AddPhotos(live.Id, 1);
    }

    [Fact]
    public async Task Cancel_LiveWithoutBids_IsCancelledAndRemovedFromBidAndSearch()
    {
        var live = await PublishedHiluxBumper();

        var tracked = await fixture.Host.ExecuteAndWaitAsync(() =>
            fixture.Host.Scenario(s =>
            {
                s.WithClaim(new Claim("username", "bob"));
                s.Post.Json(new { }).ToUrl($"/api/listings/{live.Id}/cancel");
                s.StatusCodeShouldBe(HttpStatusCode.NoContent);
            }));

        Assert.Equal(live.Id, tracked.Sent.SingleMessage<AuctionDeleted>().Id);
        var mine = await Send<List<MyListingDto>>(s => s.Get.Url("/api/listings/mine"));
        Assert.Equal("Cancelled", mine.Single(x => x.Id == live.Id).State);
    }

    [Fact]
    public async Task Cancel_Draft_DeletesIt()
    {
        var draft = await CreateDraft("CommonPart", "Dash cam");
        await AddPhotos(draft.Id, 1);

        await SendText(s => s.Post.Json(new { }).ToUrl($"/api/listings/{draft.Id}/cancel"), HttpStatusCode.NoContent);

        using var scope = fixture.Host.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AuctionDbContext>().Auctions.AnyAsync(a => a.Id == draft.Id));
    }

    [Fact]
    public async Task EndedNoBids_RelistWithOneTap()
    {
        var live = await PublishedHiluxBumper();
        using (var scope = fixture.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
            (await db.Auctions.FindAsync(live.Id))!.Status = Status.Unsold;
            await db.SaveChangesAsync();
        }

        var mine = await Send<List<MyListingDto>>(s => s.Get.Url("/api/listings/mine"));
        Assert.Equal("EndedNoBids", mine.Single(x => x.Id == live.Id).State);

        var relisted = await Send<AuctionDto>(s => s.Post.Json(new { }).ToUrl($"/api/listings/{live.Id}/relist"), status: HttpStatusCode.Created);
        Assert.Equal("Live", relisted.Status);
        Assert.Equal(live.Title, relisted.Title);
        Assert.Equal(3, relisted.ImageItems.Count);
        Assert.Single(relisted.Fitments);
    }

    [Fact]
    public async Task CopyFitmentFromMyLastListing()
    {
        await PublishedHiluxBumper();

        var rows = await Send<List<FitmentDto>>(s => s.Get.Url("/api/listings/last-fitment"));

        Assert.Equal("Hilux", Assert.Single(rows).Model);
    }

    [Fact]
    public async Task Vehicle_FullVinOnlyForTheSeller()
    {
        var draft = await CreateDraft("Vehicle", null);
        await Save(draft.Id, new SaveListingDto { Year = 2019, Make = "Maruti Suzuki", Model = "Swift", Vehicle = new VehicleDetailsDto { Vin = "MA3EWDE1S00123456" } });

        var mine = await Send<AuctionDto>(s => s.Get.Url($"/api/listings/{draft.Id}"));
        Assert.Equal("MA3EWDE1S00123456", mine.Vehicle!.Vin);

        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        (await db.Auctions.FindAsync(draft.Id))!.Status = Status.Live;
        await db.SaveChangesAsync();
        var theirs = await Send<AuctionDto>(s => s.Get.Url($"/api/auctions/{draft.Id}"), "alice");
        Assert.Equal("••••••123456", theirs.Vehicle!.Vin);
    }

    // ── catalogue ──

    [Theory]
    [InlineData("bonnet", "Bonnet")]
    [InlineData("hood", "Bonnet")]
    [InlineData("dickey", "Boot lid")]
    [InlineData("silencer", "Silencer")]
    [InlineData("muffler", "Silencer")]
    [InlineData("tire", "Tyre")]
    [InlineData("windshield", "Windscreen")]
    [InlineData("wing", "Fender")]
    [InlineData("number plate", "Licence plate frame")]
    [InlineData("transmission", "Gearbox")]
    [InlineData("turn signal", "Indicator")]
    public async Task PartTypeSearch_UnderstandsSynonymsAndLocalWords(string text, string expected)
    {
        var types = await Send<List<PartTypeDto>>(s => s.Get.Url($"/api/catalog/part-types?q={Uri.EscapeDataString(text)}"));

        Assert.Equal(expected, types.First().Name);
    }

    [Fact]
    public async Task Catalogue_HasFitmentModesAndSpecFields()
    {
        var tyre = (await Send<List<PartTypeDto>>(s => s.Get.Url("/api/catalog/part-types?q=tyre"))).First();
        var categories = await Send<List<CategoryDto>>(s => s.Get.Url("/api/catalog/categories"));

        Assert.Equal("SizeBased", tyre.FitmentMode);
        Assert.Equal(["width", "profile", "rim", "loadIndex", "speedRating"], tyre.SpecFields.Select(f => f.Key));
        Assert.Contains(categories, c => c.Name == "Body – Exterior" && c.SubCategories.Any(s => s.PartTypes.Any(p => p.Name == "Bumper")));
    }

    [Theory]
    [InlineData("chevy", "Chevrolet")]
    [InlineData("merc", "Mercedes-Benz")]
    [InlineData("beemer", "BMW")]
    [InlineData("vw", "Volkswagen")]
    public async Task Makes_AliasesWork(string text, string expected)
    {
        var makes = await Send<List<MakeDto>>(s => s.Get.Url($"/api/catalog/makes?q={text}"));

        Assert.Equal(expected, makes.First().Name);
    }

    [Fact]
    public async Task MissingMake_RequestedIsUsableByTheRequesterOnly_UntilApproved()
    {
        var make = await Send<MakeDto>(s => s.Post.Json(new RequestMakeDto { Name = "Innoson" }).ToUrl("/api/catalog/makes/request"));
        Assert.False(make.IsApproved);

        var mine = await Send<List<MakeDto>>(s => s.Get.Url("/api/catalog/makes?q=inno"));
        var theirs = await Send<List<MakeDto>>(s => s.Get.Url("/api/catalog/makes?q=inno"), "alice");
        Assert.Single(mine);
        Assert.Empty(theirs);

        // Not an admin
        await SendText(s => s.Post.Json(new { }).ToUrl($"/api/catalog/makes/{make.Id}/approve"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Dictionary_ListsMakesModelsAndPartTypes()
    {
        var dictionary = await Send<SearchDictionaryDto>(s => s.Get.Url("/api/catalog/dictionary"));

        Assert.Contains(dictionary.Makes, m => m.Name == "Maruti Suzuki" && m.Models.Any(x => x.Name == "Swift"));
        Assert.Contains(dictionary.PartTypes, p => p.Name == "Bonnet" && p.Synonyms.Contains("hood"));
    }

    // ── orders & garage ──

    [Fact]
    public async Task Sold_PickupOnly_ReadyForPickupPickedUpThenCompletedByTheBuyer()
    {
        var live = await PublishedHiluxBumper();
        await fixture.Host.InvokeMessageAndWaitAsync(new AuctionFinished
        {
            AuctionId = live.Id, Seller = "bob", Winner = "alice", Amount = 14100, ItemSold = true, Status = "Sold",
            EndReason = "SellerAccepted"
        });

        var buyerView = Assert.Single(await Send<List<OrderDto>>(s => s.Get.Url("/api/orders"), "alice"));
        Assert.Equal("SoldAwaitingHandover", buyerView.Status);
        Assert.Equal("buyer", buyerView.Role);
        Assert.Equal("bob", buyerView.OtherParty);
        Assert.Equal(14100, buyerView.FinalTotal);
        // Part price 14,000 + winning bid 100; no shipping.
        Assert.Equal((14000, 100, 14100), (buyerView.PartPrice, buyerView.WinningBid, buyerView.Total));
        Assert.Null(buyerView.ShippingFee);
        Assert.Equal("bob@example.com", buyerView.ContactEmail);

        // The buyer opened Orders, so the seller can now see the buyer's contact too.
        var sellerView = Assert.Single(await Send<List<OrderDto>>(s => s.Get.Url("/api/orders")));
        Assert.Equal("alice@example.com", sellerView.ContactEmail);

        // Pickup was the only delivery option: no choice, and the pickup steps.
        Assert.Equal("Pickup", buyerView.DeliveryMethod);
        Assert.Equal(["Sold", "Ready for pickup", "Picked up", "Completed"], buyerView.Tracking.Select(t => t.Label));
        Assert.Empty(buyerView.DeliveryChoices);
        Assert.Equal([("Completed", "Completed")], buyerView.Actions.Select(a => (a.Status, a.Label)));
        Assert.Equal([("ReadyForPickup", "Ready for pickup")], sellerView.Actions.Select(a => (a.Status, a.Label)));
        // Shipping steps don't apply.
        await SendText(s => s.Post.Json(new OrderStatusDto { Status = "Shipped" }).ToUrl($"/api/orders/{buyerView.Id}/status"), HttpStatusCode.BadRequest);

        var ready = await Step(buyerView.Id, "ReadyForPickup");
        Assert.Equal("Ready for pickup", ready.StatusLabel);
        Assert.Equal([("PickedUp", "Picked up")], ready.Actions.Select(a => (a.Status, a.Label)));
        // The buyer can't do the seller's steps.
        await SendText(s => s.Post.Json(new OrderStatusDto { Status = "PickedUp" }).ToUrl($"/api/orders/{buyerView.Id}/status"), HttpStatusCode.Forbidden, "alice");
        var picked = await Step(buyerView.Id, "PickedUp");
        Assert.Equal("Picked up", picked.StatusLabel);
        Assert.Empty(picked.Actions); // the buyer completes it

        var completed = await Step(buyerView.Id, "Completed", "alice");
        Assert.Equal("Completed", completed.Status);
        Assert.All(completed.Tracking, t => Assert.True(t.Done));

        var mine = await Send<List<MyListingDto>>(s => s.Get.Url("/api/listings/mine"));
        Assert.Equal("Completed", mine.Single(x => x.Id == live.Id).State);

        // Strangers can't see it.
        Assert.Empty(await Send<List<OrderDto>>(s => s.Get.Url("/api/orders"), "carol"));
    }

    /// <summary>A Hilux bumper with these delivery options, sold to alice (seller bob); returns alice's view of the order.</summary>
    private async Task<OrderDto> SoldOrder(params string[] delivery)
    {
        var draft = await CreateDraft("VehicleSpecificPart", "Bumper");
        var listing = HiluxBumper();
        listing.DeliveryOptions = [.. delivery];
        if (delivery.Contains("Shipping"))
        {
            listing.SizeClass = "Large";
            listing.ShippingFee = 500;
        }
        listing.PickupAddress = "Plot 12, Road 4, KPHB Colony";
        await Save(draft.Id, listing);
        await AddPhotos(draft.Id, 3);
        var live = await Send<AuctionDto>(s => s.Post.Json(new { }).ToUrl($"/api/listings/{draft.Id}/publish"));
        await fixture.Host.InvokeMessageAndWaitAsync(new AuctionFinished
        {
            AuctionId = live.Id, Seller = "bob", Winner = "alice", Amount = 14100, ItemSold = true, Status = "Sold", EndReason = "TimeEnded"
        });
        return (await Send<List<OrderDto>>(s => s.Get.Url("/api/orders"), "alice")).Single(o => o.AuctionId == live.Id);
    }

    private Task<OrderDto> Step(string orderId, string status, string user = "bob", string? courier = null, string? tracking = null) =>
        Send<OrderDto>(s => s.Post.Json(new OrderStatusDto { Status = status, Courier = courier, TrackingNumber = tracking })
            .ToUrl($"/api/orders/{orderId}/status"), user);

    private static AddressDto Hyderabad() => new()
    {
        Name = "Alice Reddy", Phone = "+91 98480 12345", Line1 = "Flat 4, Road 2", Line2 = "Kukatpally", City = "Hyderabad",
        Postcode = "500072", CountryCode = "IN"
    };

    [Fact]
    public async Task BothOffered_BuyerChoosesShipping_WithAnAddress_ThenShippedDeliveredCompleted()
    {
        var order = await SoldOrder("Pickup", "Shipping");
        Assert.Null(order.DeliveryMethod);
        Assert.Equal(["Pickup", "Shipping"], order.DeliveryChoices);
        Assert.Equal("Sold", order.StatusLabel);
        Assert.Equal(500, order.ShippingFee);
        Assert.Equal(14100, order.Total); // shipping only counts once chosen

        // The seller has to wait for the buyer's choice.
        var seller = (await Send<List<OrderDto>>(s => s.Get.Url("/api/orders"))).Single(o => o.Id == order.Id);
        Assert.Empty(seller.Actions);
        await SendText(s => s.Post.Json(new OrderStatusDto { Status = "Shipped" }).ToUrl($"/api/orders/{order.Id}/status"), HttpStatusCode.BadRequest);

        // Shipping needs a valid address in the seller's country; only offered options are accepted; buyer only.
        await SendText(s => s.Post.Json(new ChooseDeliveryDto { Method = "Shipping" }).ToUrl($"/api/orders/{order.Id}/delivery-method"), HttpStatusCode.BadRequest, "alice");
        var badPin = Hyderabad();
        badPin.Postcode = "5000";
        var pinError = await SendText(s => s.Post.Json(new ChooseDeliveryDto { Method = "Shipping", Address = badPin }).ToUrl($"/api/orders/{order.Id}/delivery-method"), HttpStatusCode.BadRequest, "alice");
        Assert.Contains("postcode", pinError);
        var abroad = Hyderabad();
        (abroad.City, abroad.Postcode, abroad.CountryCode) = ("Houston", "77002", "US");
        var abroadError = await SendText(s => s.Post.Json(new ChooseDeliveryDto { Method = "Shipping", Address = abroad }).ToUrl($"/api/orders/{order.Id}/delivery-method"), HttpStatusCode.BadRequest, "alice");
        Assert.Contains("within India", abroadError);
        await SendText(s => s.Post.Json(new ChooseDeliveryDto { Method = "BuyerArranges" }).ToUrl($"/api/orders/{order.Id}/delivery-method"), HttpStatusCode.BadRequest, "alice");
        await SendText(s => s.Post.Json(new ChooseDeliveryDto { Method = "Pickup" }).ToUrl($"/api/orders/{order.Id}/delivery-method"), HttpStatusCode.Forbidden);

        var chosen = await Send<OrderDto>(s => s.Post.Json(new ChooseDeliveryDto { Method = "Shipping", Address = Hyderabad() })
            .ToUrl($"/api/orders/{order.Id}/delivery-method"), "alice");
        Assert.Equal("Shipping", chosen.DeliveryMethod);
        Assert.Equal("Alice Reddy", chosen.ShipTo!.Name);
        Assert.Null(chosen.PickupAddress);
        Assert.Equal(14600, chosen.Total); // + shipping ₹500
        Assert.Equal(["Sold", "Shipped", "Delivered", "Completed"], chosen.Tracking.Select(t => t.Label));
        Assert.Equal([("Delivered", "Delivered")], chosen.Actions.Select(a => (a.Status, a.Label)));

        // The address was saved to alice's address book as her default.
        var book = await Send<List<ShippingAddressDto>>(s => s.Get.Url("/api/shipping-addresses"), "alice");
        Assert.True(Assert.Single(book).IsDefault);

        // The buyer can't do the seller's steps.
        await SendText(s => s.Post.Json(new OrderStatusDto { Status = "Shipped" }).ToUrl($"/api/orders/{order.Id}/status"), HttpStatusCode.Forbidden, "alice");

        var shipped = await Step(order.Id, "Shipped", courier: "Delhivery", tracking: "AWB123456");
        Assert.Equal("Shipped", shipped.StatusLabel);
        Assert.Equal("Delhivery", shipped.Courier);
        Assert.Equal("Delhivery · Tracking AWB123456", shipped.Tracking.Single(t => t.Status == "Shipped").Note);
        Assert.Contains("Flat 4, Road 2", shipped.DeliveryAddress);
        Assert.Empty(shipped.Actions); // the buyer confirms the delivery

        // Never backwards, and no more choosing once shipped.
        await SendText(s => s.Post.Json(new OrderStatusDto { Status = "Shipped" }).ToUrl($"/api/orders/{order.Id}/status"), HttpStatusCode.BadRequest);
        await SendText(s => s.Post.Json(new ChooseDeliveryDto { Method = "Pickup" }).ToUrl($"/api/orders/{order.Id}/delivery-method"), HttpStatusCode.BadRequest, "alice");

        // Confirming the delivery completes the order.
        var delivered = await Step(order.Id, "Delivered", "alice");
        Assert.Equal("Completed", delivered.Status);
        Assert.All(delivered.Tracking, t => Assert.True(t.Done));
        Assert.NotNull(delivered.Tracking.Single(t => t.Status == "Delivered").At);
        Assert.Empty(delivered.Actions);
    }

    [Fact]
    public async Task ShippingOnly_NoPickupChoice_TheBuyerMustAddAnAddressBeforeTheSellerShips()
    {
        var order = await SoldOrder("Shipping");
        Assert.Equal("Shipping", order.DeliveryMethod);
        Assert.True(order.NeedsShippingAddress);
        Assert.Equal(["Shipping"], order.DeliveryChoices);
        Assert.Empty(order.Actions);
        Assert.Equal(14600, order.Total);

        await SendText(s => s.Post.Json(new OrderStatusDto { Status = "Shipped" }).ToUrl($"/api/orders/{order.Id}/status"), HttpStatusCode.BadRequest);
        await SendText(s => s.Post.Json(new ChooseDeliveryDto { Method = "Pickup" }).ToUrl($"/api/orders/{order.Id}/delivery-method"), HttpStatusCode.BadRequest, "alice");

        // From the address book.
        var saved = await Send<ShippingAddressDto>(s => s.Post.Json(new ShippingAddressDto
        {
            Name = "Alice Reddy", Phone = "9848012345", Line1 = "Flat 4, Road 2", City = "Hyderabad", Postcode = "500072", CountryCode = "IN"
        }).ToUrl("/api/shipping-addresses"), "alice", HttpStatusCode.Created);
        var withAddress = await Send<OrderDto>(s => s.Post.Json(new ChooseDeliveryDto { Method = "Shipping", AddressId = saved.Id })
            .ToUrl($"/api/orders/{order.Id}/delivery-method"), "alice");
        Assert.False(withAddress.NeedsShippingAddress);
        Assert.Empty(withAddress.DeliveryChoices);

        var seller = (await Send<List<OrderDto>>(s => s.Get.Url("/api/orders"))).Single(o => o.Id == order.Id);
        Assert.Equal([("Shipped", "Shipped")], seller.Actions.Select(a => (a.Status, a.Label)));
        Assert.Equal("Hyderabad", seller.ShipTo!.City);
    }

    [Fact]
    public async Task Pickup_TheExactAddressIsOnlySharedWithTheBuyerAfterTheSale()
    {
        var order = await SoldOrder("Pickup");

        Assert.Equal("Plot 12, Road 4, KPHB Colony", order.PickupAddress);
        // The public listing only has the city.
        var listing = await Send<AuctionDto>(s => s.Get.Url($"/api/auctions/{order.AuctionId}"), "carol");
        Assert.Equal("Hyderabad", listing.PickupCity);
        Assert.Null(listing.PickupAddress);
    }

    [Fact]
    public async Task Buyer_CanConfirmReceived_EvenIfTheSellerNeverUpdated()
    {
        var order = await SoldOrder("Pickup");

        var done = await Step(order.Id, "Completed", "alice");

        Assert.Equal("Completed", done.Status);
        Assert.NotNull(done.CompletedAt);
        // Strangers can't touch it.
        await SendText(s => s.Post.Json(new OrderStatusDto { Status = "Completed" }).ToUrl($"/api/orders/{order.Id}/status"), HttpStatusCode.NotFound, "carol");
    }

    [Fact]
    public async Task Garage_AddListDelete()
    {
        var added = await Send<GarageVehicleDto>(s => s.Post.Json(new GarageVehicleDto { Year = 2018, Make = "Toyota", Model = "Hilux" }).ToUrl("/api/garage"));
        Assert.Single(await Send<List<GarageVehicleDto>>(s => s.Get.Url("/api/garage")));
        Assert.Empty(await Send<List<GarageVehicleDto>>(s => s.Get.Url("/api/garage"), "alice"));

        await SendText(s => s.Delete.Url($"/api/garage/{added.Id}"), HttpStatusCode.NoContent);
        Assert.Empty(await Send<List<GarageVehicleDto>>(s => s.Get.Url("/api/garage")));
    }
}
