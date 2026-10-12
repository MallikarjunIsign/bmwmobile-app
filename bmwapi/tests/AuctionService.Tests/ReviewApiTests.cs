using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Alba;
using AuctionService.Controllers;
using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using AuctionService.Handlers;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Tracking;

namespace AuctionService.Tests;

/// <summary>Ratings & reviews: verified purchase, time limits, seller reply, report / hide, the seller summary.</summary>
[Collection("auction-service")]
public class ReviewApiTests(AppFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        db.ReviewReports.RemoveRange(db.ReviewReports);
        db.Reviews.RemoveRange(db.Reviews);
        db.SellerRatings.RemoveRange(db.SellerRatings);
        db.Orders.RemoveRange(db.Orders);
        db.Auctions.RemoveRange(db.Auctions);
        db.MediaFiles.RemoveRange(db.MediaFiles);
        await db.SaveChangesAsync();
    }

    private async Task<T> Send<T>(Action<Scenario> configure, string user = "alice", HttpStatusCode status = HttpStatusCode.OK)
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", user));
            configure(s);
            s.StatusCodeShouldBe(status);
        });
        return await result.ReadAsJsonAsync<T>();
    }

    private async Task<string> SendText(Action<Scenario> configure, string user, HttpStatusCode status)
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.WithClaim(new Claim("username", user));
            configure(s);
            s.StatusCodeShouldBe(status);
        });
        return await result.ReadAsTextAsync();
    }

    /// <summary>bob's listing sold to alice; the order is Completed (completedDaysAgo days ago) unless stated otherwise.</summary>
    private async Task<Order> SoldOrder(OrderStatus status = OrderStatus.Completed, int completedDaysAgo = 0)
    {
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        var id = Guid.NewGuid().ToString();
        db.Auctions.Add(new Auction
        {
            Id = id, Seller = "bob", Status = Status.Sold, StartingPrice = 1000, ReservePrice = 1000, AuctionEnd = DateTime.UtcNow,
            Item = new Item { Id = id, Kind = ListingKind.CommonPart, ItemType = ItemType.Part, Make = "", Model = "", Description = "", DeliveryOptions = DeliveryOptions.Pickup }
        });
        var order = new Order
        {
            AuctionId = id, Seller = "bob", Buyer = "alice", Title = "Dash Cam – 70mai – New", FinalTotal = 1200, PartPrice = 1000,
            DeliveryOptions = DeliveryOptions.Pickup, DeliveryMethod = DeliveryOptions.Pickup, Status = status,
            CompletedAt = status == OrderStatus.Completed ? DateTime.UtcNow.AddDays(-completedDaysAgo) : null
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static SaveReviewDto Good(string orderId) => new()
    {
        OrderId = orderId, ItemRating = 5, SellerRating = 4, Communication = 5, Accuracy = 4, Delivery = 3,
        Tags = ["Fast replies", "As described"], Comment = "Exactly as shown, quick pickup."
    };

    private Task<ReviewDto> Write(SaveReviewDto dto) =>
        Send<ReviewDto>(s => s.Post.Json(dto).ToUrl("/api/reviews"), status: HttpStatusCode.Created);

    [Fact]
    public async Task Buyer_ReviewsACompletedOrder_TheSellerRatingIsRecalculatedAndAnnounced()
    {
        var order = await SoldOrder();

        ReviewDto? review = null;
        var tracked = await fixture.Host.ExecuteAndWaitAsync(async () => review = await Write(Good(order.Id)));

        Assert.True(review!.IsMine);
        Assert.True(review.CanEdit);
        Assert.Equal("alice", review.Buyer);
        var changed = tracked.Sent.SingleMessage<SellerRatingChanged>();
        Assert.Equal(("bob", 4.0, 1), (changed.Seller, changed.Average, changed.Count));

        // The listing carries the rating (cards, details, search).
        var listing = await Send<AuctionDto>(s => s.Get.Url($"/api/auctions/{order.AuctionId}"));
        Assert.Equal((4.0, 1), (listing.SellerRating, listing.SellerRatingCount));

        // A second review of the same order is refused.
        var again = await SendText(s => s.Post.Json(Good(order.Id)).ToUrl("/api/reviews"), "alice", HttpStatusCode.BadRequest);
        Assert.Contains("already reviewed", again);

        // The order now links the review and can't be reviewed again.
        var mine = (await Send<List<OrderDto>>(s => s.Get.Url("/api/orders"))).Single(o => o.Id == order.Id);
        Assert.Equal((review.Id, false), (mine.ReviewId, mine.CanReview));
    }

    [Fact]
    public async Task OnlyTheBuyerOfACompletedOrder_Within30Days()
    {
        var open = await SoldOrder(OrderStatus.Shipped);
        var completed = await SoldOrder();
        var late = await SoldOrder(completedDaysAgo: 31);

        Assert.Contains("once the order is completed",
            await SendText(s => s.Post.Json(Good(open.Id)).ToUrl("/api/reviews"), "alice", HttpStatusCode.BadRequest));
        await SendText(s => s.Post.Json(Good(completed.Id)).ToUrl("/api/reviews"), "bob", HttpStatusCode.Forbidden); // the seller
        await SendText(s => s.Post.Json(Good(completed.Id)).ToUrl("/api/reviews"), "carol", HttpStatusCode.NotFound); // a stranger
        Assert.Contains("30 days", await SendText(s => s.Post.Json(Good(late.Id)).ToUrl("/api/reviews"), "alice", HttpStatusCode.BadRequest));

        var orders = await Send<List<OrderDto>>(s => s.Get.Url("/api/orders"));
        Assert.True(orders.Single(o => o.Id == completed.Id).CanReview);
        Assert.False(orders.Single(o => o.Id == open.Id).CanReview);
        Assert.False(orders.Single(o => o.Id == late.Id).CanReview);
    }

    [Fact]
    public async Task Validation_StarsTagsCommentAndPhotos()
    {
        var order = await SoldOrder();
        var bad = Good(order.Id);
        (bad.ItemRating, bad.Communication, bad.Tags, bad.Comment) = (0, 6, ["Rude"], "Call me on 98480 12345");

        var body = await SendText(s => s.Post.Json(bad).ToUrl("/api/reviews"), "alice", HttpStatusCode.BadRequest);
        foreach (var field in new[] { "itemRating", "communication", "tags", "comment" }) Assert.Contains(field, body);

        // Photos must be the buyer's own review uploads.
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(TestImages.Jpeg());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(content, "files", "a.jpg");
        var bobsPhoto = await Send<List<MediaDto>>(s => s.Post.MultipartFormData(form).ToUrl("/api/media").QueryString("purpose", "review"), "bob");
        var withBobs = Good(order.Id);
        withBobs.PhotoIds = [bobsPhoto[0].Id];
        Assert.Contains("uploaded by you", await SendText(s => s.Post.Json(withBobs).ToUrl("/api/reviews"), "alice", HttpStatusCode.BadRequest));

        var form2 = new MultipartFormDataContent();
        var content2 = new ByteArrayContent(TestImages.Jpeg());
        content2.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form2.Add(content2, "files", "b.jpg");
        var myPhoto = await Send<List<MediaDto>>(s => s.Post.MultipartFormData(form2).ToUrl("/api/media").QueryString("purpose", "review"));
        var withMine = Good(order.Id);
        withMine.PhotoIds = [myPhoto[0].Id];
        var review = await Write(withMine);
        Assert.Equal(myPhoto[0].Url, Assert.Single(review.Photos).Url);
    }

    [Fact]
    public async Task Edit_Within7DaysOfPosting()
    {
        var order = await SoldOrder();
        var review = await Write(Good(order.Id));

        var changed = Good(order.Id);
        changed.SellerRating = 2;
        changed.Tags = ["Slow to respond"];
        var edited = await Send<ReviewDto>(s => s.Put.Json(changed).ToUrl($"/api/reviews/{review.Id}"));
        Assert.Equal((2, "Slow to respond"), (edited.SellerRating, Assert.Single(edited.Tags)));
        await SendText(s => s.Put.Json(changed).ToUrl($"/api/reviews/{review.Id}"), "bob", HttpStatusCode.Forbidden);

        using (var scope = fixture.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
            (await db.Reviews.FindAsync(review.Id))!.CreatedAt = DateTime.UtcNow.AddDays(-8);
            await db.SaveChangesAsync();
        }
        Assert.Contains("7 days", await SendText(s => s.Put.Json(changed).ToUrl($"/api/reviews/{review.Id}"), "alice", HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Seller_PostsOnePublicReply()
    {
        var order = await SoldOrder();
        var review = await Write(Good(order.Id));

        await SendText(s => s.Post.Json(new ReviewReplyDto { Text = "Thanks!" }).ToUrl($"/api/reviews/{review.Id}/reply"), "alice", HttpStatusCode.Forbidden);
        var replied = await Send<ReviewDto>(s => s.Post.Json(new ReviewReplyDto { Text = "Thanks for buying!" }).ToUrl($"/api/reviews/{review.Id}/reply"), "bob");
        Assert.Equal("Thanks for buying!", replied.SellerReply);
        Assert.False(replied.CanReply);
        await SendText(s => s.Post.Json(new ReviewReplyDto { Text = "Again" }).ToUrl($"/api/reviews/{review.Id}/reply"), "bob", HttpStatusCode.BadRequest);

        // Everyone sees the reply; the buyer's name is partly hidden for others.
        var forCarol = await Send<List<ReviewDto>>(s => s.Get.Url($"/api/reviews/listing/{order.AuctionId}"), "carol");
        Assert.Equal(("a***e", "Thanks for buying!"), (forCarol.Single().Buyer, forCarol.Single().SellerReply));
    }

    [Fact]
    public async Task SellerProfile_AverageBreakdownQuickRatingsSalesAndLatestReviews()
    {
        var first = await SoldOrder();
        var second = await SoldOrder();
        await SoldOrder(OrderStatus.Shipped); // not completed: not a completed sale
        await Write(Good(first.Id));
        var low = Good(second.Id);
        (low.SellerRating, low.Communication, low.Accuracy, low.Delivery) = (2, 3, null, null);
        await Write(low);

        var profile = await Send<SellerRatingDto>(s => s.Get.Url("/api/sellers/bob"), "carol");

        Assert.Equal((3.0, 2, true), (profile.Average, profile.Count, profile.IsNewSeller));
        Assert.Equal([0, 1, 0, 1, 0], profile.Breakdown); // 5★ 4★ 3★ 2★ 1★
        Assert.Equal((4.0, 4.0, 3.0), (profile.CommunicationAverage, profile.AccuracyAverage, profile.DeliveryAverage));
        Assert.Equal(2, profile.CompletedSales);
        Assert.Equal(2, profile.LatestReviews.Count);
        Assert.All(profile.LatestReviews, r => Assert.Equal("a***e", r.Buyer));

        var nobody = await Send<SellerRatingDto>(s => s.Get.Url("/api/sellers/nobody"), "carol");
        Assert.Equal((null, 0, true), (nobody.Average, nobody.Count, nobody.IsNewSeller));
    }

    [Fact]
    public async Task Report_AndAdminHide_RemovesItFromTheProfileAndTheRating()
    {
        var order = await SoldOrder();
        var review = await Write(Good(order.Id));

        await SendText(s => s.Post.Json(new ReviewReportDto { Reason = "rude" }).ToUrl($"/api/reviews/{review.Id}/report"), "carol", HttpStatusCode.BadRequest);
        await SendText(s => s.Post.Json(new ReviewReportDto { Reason = "fake", Details = "Never bought it" }).ToUrl($"/api/reviews/{review.Id}/report"), "carol", HttpStatusCode.NoContent);

        await SendText(s => s.Get.Url("/api/reviews/admin/reports"), "carol", HttpStatusCode.Forbidden);
        Assert.Contains("Never bought it", await SendText(s => s.Get.Url("/api/reviews/admin/reports"), "admin", HttpStatusCode.OK));

        await SendText(s => s.Post.Json(new { }).ToUrl($"/api/reviews/{review.Id}/hide"), "bob", HttpStatusCode.Forbidden);
        var hidden = await fixture.Host.ExecuteAndWaitAsync(() =>
            SendText(s => s.Post.Json(new { }).ToUrl($"/api/reviews/{review.Id}/hide"), "admin", HttpStatusCode.NoContent));
        Assert.Equal(0, hidden.Sent.SingleMessage<SellerRatingChanged>().Count);

        var profile = await Send<SellerRatingDto>(s => s.Get.Url("/api/sellers/bob"), "carol");
        Assert.Equal((null, 0), (profile.Average, profile.Count));
        Assert.Empty(profile.LatestReviews);
        await SendText(s => s.Get.Url($"/api/reviews/{review.Id}"), "carol", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reminder_2DaysAfterCompletion_OnlyWhileNotReviewed()
    {
        var order = await SoldOrder(OrderStatus.ReadyForPickup);

        // Completing the order schedules the reminder.
        var completed = await fixture.Host.ExecuteAndWaitAsync(() =>
            Send<OrderDto>(s => s.Post.Json(new OrderStatusDto { Status = "Completed" }).ToUrl($"/api/orders/{order.Id}/status")));
        var scheduled = completed.Scheduled.SingleMessage<ReviewReminder>();
        Assert.Equal(order.Id, scheduled.OrderId);

        var due = await fixture.Host.InvokeMessageAndWaitAsync(new ReviewReminder(order.Id));
        var requested = due.Sent.SingleMessage<ReviewRequested>();
        Assert.Equal(("alice", "Dash Cam – 70mai – New"), (requested.Buyer, requested.Title));

        await Write(Good(order.Id));
        var afterReview = await fixture.Host.InvokeMessageAndWaitAsync(new ReviewReminder(order.Id));
        Assert.Empty(afterReview.Sent.MessagesOf<ReviewRequested>());
    }
}
