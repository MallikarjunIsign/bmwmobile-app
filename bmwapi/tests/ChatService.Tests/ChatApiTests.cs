using System.Net;
using System.Security.Claims;
using Alba;
using ChatService.Controllers;
using ChatService.Data;
using ChatService.Services;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Tracking;

namespace ChatService.Tests;

[Collection("chat-service")]
public class ChatApiTests(AppFixture fixture) : IAsyncLifetime
{
    // bob sells; alice and carol are buyers; dave is a stranger.
    private readonly string _listing = Guid.NewGuid().ToString();

    public Task InitializeAsync()
    {
        fixture.Listings.Listings[_listing] = new ListingInfo(_listing, "bob", "Bob Kumar", "2016–2020 Toyota Hilux Front Bumper",
            "http://gateway.test/images/abcdef1234567890.jpg", 14000, null, "INR", "Live", "Auction", null);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        await db.Messages.ExecuteDeleteAsync();
        await db.Reports.ExecuteDeleteAsync();
        await db.Conversations.ExecuteDeleteAsync();
        await db.ListingBuyers.ExecuteDeleteAsync();
        await db.Listings.ExecuteDeleteAsync();
        await db.Blocks.ExecuteDeleteAsync();
        await db.PushDevices.ExecuteDeleteAsync();
        await db.UserRatings.ExecuteDeleteAsync();
        // The rate limit counts real time: start every test with a clean minute.
    }

    private async Task<T> Send<T>(Action<Scenario> configure, string user, HttpStatusCode status = HttpStatusCode.OK)
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

    private Task<ConversationDto> Start(string user, string? buyer = null, string? listing = null) =>
        Send<ConversationDto>(s => s.Post.Json(new StartConversationDto { ListingId = listing ?? _listing, Buyer = buyer }).ToUrl("/api/chat/conversations"), user);

    private Task<MessageDto> Message(string conversationId, string user, string text) =>
        Send<MessageDto>(s => s.Post.Json(new SendMessageDto { Text = text }).ToUrl($"/api/chat/conversations/{conversationId}/messages"), user);

    private Task<List<ConversationSummaryDto>> Inbox(string user, string role = "all") =>
        Send<List<ConversationSummaryDto>>(s => s.Get.Url($"/api/chat/conversations?role={role}"), user);

    [Fact]
    public async Task Buyer_StartsAChatFromTheListing_OnePerListingAndBuyer_WithTheListingHeaderAndSafetyTip()
    {
        var first = await Start("alice");
        var again = await Start("alice");

        Assert.Equal(first.Id, again.Id);
        Assert.Equal(("alice", "bob", "bob", "buying"), (first.Buyer, first.Seller, first.OtherUser, first.Role));
        Assert.Equal("2016–2020 Toyota Hilux Front Bumper", first.Listing.Title);
        Assert.Equal(14000, first.Listing.Price);
        Assert.Equal("Live", first.Listing.Status);
        Assert.True(first.ShowSafetyTip);
        Assert.Empty(first.Messages);

        // Another buyer gets their own conversation; an empty chat isn't in the inbox yet.
        Assert.NotEqual(first.Id, (await Start("carol")).Id);
        Assert.Empty(await Inbox("bob"));
    }

    [Fact]
    public async Task Seller_CantStartAChatWithAStrangerOrThemselves_ButRepliesToChatsTheyGet()
    {
        await SendText(s => s.Post.Json(new StartConversationDto { ListingId = _listing }).ToUrl("/api/chat/conversations"), "bob", HttpStatusCode.BadRequest);
        await SendText(s => s.Post.Json(new StartConversationDto { ListingId = _listing, Buyer = "bob" }).ToUrl("/api/chat/conversations"), "bob", HttpStatusCode.BadRequest);
        await SendText(s => s.Post.Json(new StartConversationDto { ListingId = _listing, Buyer = "dave" }).ToUrl("/api/chat/conversations"), "bob", HttpStatusCode.Forbidden);

        var chat = await Start("alice");
        await Message(chat.Id, "alice", "Is this still available?");
        var sellerView = await Start("bob", buyer: "alice");
        Assert.Equal(chat.Id, sellerView.Id);
        Assert.Equal("selling", sellerView.Role);
        await Message(chat.Id, "bob", "Yes, it is");
    }

    [Fact]
    public async Task OnlyTheTwoParticipantsCanReadOrWrite()
    {
        var chat = await Start("alice");
        await Message(chat.Id, "alice", "Hello");

        await SendText(s => s.Get.Url($"/api/chat/conversations/{chat.Id}"), "dave", HttpStatusCode.NotFound);
        await SendText(s => s.Post.Json(new SendMessageDto { Text = "hi" }).ToUrl($"/api/chat/conversations/{chat.Id}/messages"), "dave", HttpStatusCode.NotFound);
        await SendText(s => s.Post.Json(new { }).ToUrl($"/api/chat/conversations/{chat.Id}/read"), "carol", HttpStatusCode.NotFound);
        Assert.Empty(await Inbox("dave"));
    }

    [Fact]
    public async Task Messages_TextUpTo1000Characters_AndUpTo5PhotosUploadedThroughTheApp()
    {
        var chat = await Start("alice");
        var url = $"/api/chat/conversations/{chat.Id}/messages";

        await SendText(s => s.Post.Json(new SendMessageDto { Text = "  " }).ToUrl(url), "alice", HttpStatusCode.BadRequest);
        await SendText(s => s.Post.Json(new SendMessageDto { Text = new string('a', 1001) }).ToUrl(url), "alice", HttpStatusCode.BadRequest);
        var six = Enumerable.Range(0, 6).Select(i => new PhotoDto($"http://10.0.2.2:6001/images/photo{i:D8}.jpg", null)).ToList();
        await SendText(s => s.Post.Json(new SendMessageDto { Photos = six }).ToUrl(url), "alice", HttpStatusCode.BadRequest);
        var foreign = await SendText(s => s.Post.Json(new SendMessageDto { Photos = [new PhotoDto("https://evil.example/x.jpg", null)] }).ToUrl(url), "alice", HttpStatusCode.BadRequest);
        Assert.Contains("uploaded through the app", foreign);

        // The photo is stored as its /images path, whatever host the phone used, and shown on the gateway's.
        var photo = await Send<MessageDto>(s => s.Post.Json(new SendMessageDto
        {
            Text = new string('a', 1000),
            Photos = [new PhotoDto("http://10.0.2.2:6001/images/0123456789abcdef.jpg", "http://10.0.2.2:6001/images/0123456789abcdef-t.jpg")]
        }).ToUrl(url), "alice");
        Assert.Equal("http://gateway.test/images/0123456789abcdef.jpg", Assert.Single(photo.Photos).Url);
        Assert.Equal("sent", photo.State);
    }

    [Fact]
    public async Task Sending_GoesLiveToTheRecipient_AndPushesToTheirPhones()
    {
        await SendText(s => s.Post.Json(new DeviceDto { Token = "bob-phone", Platform = "android" }).ToUrl("/api/chat/devices"), "bob", HttpStatusCode.NoContent);
        await SendText(s => s.Post.Json(new DeviceDto { Token = "dead-old-phone" }).ToUrl("/api/chat/devices"), "bob", HttpStatusCode.NoContent);
        var chat = await Start("alice");

        var tracked = await fixture.Host.ExecuteAndWaitAsync(() => Message(chat.Id, "alice", "Does it fit my 2018 Toyota Hilux?"));

        var sent = tracked.Sent.SingleMessage<ChatMessageSent>();
        Assert.Equal(("alice", "bob", "Does it fit my 2018 Toyota Hilux?"), (sent.Sender, sent.Recipient, sent.Preview));
        var push = fixture.Push.Sent.Last(p => p.Tokens.Contains("bob-phone"));
        Assert.Equal("New message from alice", push.Message.Title);
        Assert.Equal("About 2016–2020 Toyota Hilux Front Bumper: Does it fit my 2018 Toyota Hilux?", push.Message.Body);
        Assert.Equal(chat.Id, push.Message.Data["conversationId"]);

        // The uninstalled phone's token was removed.
        using var scope = fixture.Host.Services.CreateScope();
        var tokens = await scope.ServiceProvider.GetRequiredService<ChatDbContext>().PushDevices.Select(d => d.Token).ToListAsync();
        Assert.Equal(["bob-phone"], tokens);
    }

    [Fact]
    public async Task Inbox_NewestFirst_FilterBuyingSelling_UnreadCount_DeliveredThenReadTicks()
    {
        var aliceChat = await Start("alice");
        await Message(aliceChat.Id, "alice", "Is this still available?");
        var carolChat = await Start("carol");
        await Message(carolChat.Id, "carol", "Can you send more photos?");
        await Message(carolChat.Id, "carol", "Please");

        Assert.Equal(3, (await Send<UnreadDto>(s => s.Get.Url("/api/chat/unread"), "bob")).Count);

        // Opening the inbox delivers them (the buyers see two ticks).
        var tracked = await fixture.Host.ExecuteAndWaitAsync(async () =>
        {
            var inbox = await Inbox("bob");
            Assert.Equal([carolChat.Id, aliceChat.Id], inbox.Select(c => c.Id));
            Assert.Equal(("carol", 2, "Please", "selling"), (inbox[0].OtherUser, inbox[0].UnreadCount, inbox[0].LastMessage, inbox[0].Role));
        });
        Assert.Contains(tracked.Sent.MessagesOf<ChatReceipt>(), r => r is { NotifyUser: "carol", State: "Delivered" });
        Assert.Equal("delivered", (await Send<ConversationDto>(s => s.Get.Url($"/api/chat/conversations/{carolChat.Id}"), "carol")).Messages[0].State);

        Assert.Empty(await Inbox("bob", "buying"));
        Assert.Single(await Inbox("alice", "buying"));

        // Reading clears the badge and turns the ticks blue.
        var read = await fixture.Host.ExecuteAndWaitAsync(() =>
            fixture.Host.Scenario(s =>
            {
                s.WithClaim(new Claim("username", "bob"));
                s.Post.Json(new { }).ToUrl($"/api/chat/conversations/{carolChat.Id}/read");
                s.StatusCodeShouldBe(HttpStatusCode.NoContent);
            }));
        Assert.Equal("Read", read.Sent.SingleMessage<ChatReceipt>().State);
        Assert.Equal(1, (await Send<UnreadDto>(s => s.Get.Url("/api/chat/unread"), "bob")).Count);
        var carolView = await Send<ConversationDto>(s => s.Get.Url($"/api/chat/conversations/{carolChat.Id}"), "carol");
        Assert.All(carolView.Messages, m => Assert.Equal("read", m.State));
    }

    [Fact]
    public async Task Block_NoMessagesEitherWay_NoNewChat_AndBidServiceIsTold()
    {
        var chat = await Start("alice");
        await Message(chat.Id, "alice", "Is this still available?");

        await SendText(s => s.Post.Json(new BlockDto { Username = "dave" }).ToUrl("/api/chat/blocks"), "bob", HttpStatusCode.NotFound);
        var tracked = await fixture.Host.ExecuteAndWaitAsync(() =>
            fixture.Host.Scenario(s =>
            {
                s.WithClaim(new Claim("username", "bob"));
                s.Post.Json(new BlockDto { Username = "alice" }).ToUrl("/api/chat/blocks");
                s.StatusCodeShouldBe(HttpStatusCode.NoContent);
            }));
        Assert.Equal(("bob", "alice"), (tracked.Sent.SingleMessage<UserBlocked>().Blocker, tracked.Sent.SingleMessage<UserBlocked>().Blocked));

        await SendText(s => s.Post.Json(new SendMessageDto { Text = "Hello?" }).ToUrl($"/api/chat/conversations/{chat.Id}/messages"), "alice", HttpStatusCode.Forbidden);
        var own = await SendText(s => s.Post.Json(new SendMessageDto { Text = "Hi" }).ToUrl($"/api/chat/conversations/{chat.Id}/messages"), "bob", HttpStatusCode.BadRequest);
        Assert.Contains("unblock", own);
        var view = await Send<ConversationDto>(s => s.Get.Url($"/api/chat/conversations/{chat.Id}"), "bob");
        Assert.True(view.BlockedByMe);

        // No new chat about bob's other listings.
        var other = Guid.NewGuid().ToString();
        fixture.Listings.Listings[other] = fixture.Listings.Listings[_listing] with { Id = other };
        await SendText(s => s.Post.Json(new StartConversationDto { ListingId = other }).ToUrl("/api/chat/conversations"), "alice", HttpStatusCode.Forbidden);

        Assert.Equal(["alice"], await Send<List<string>>(s => s.Get.Url("/api/chat/blocks"), "bob"));
        var unblocked = await fixture.Host.ExecuteAndWaitAsync(() =>
            fixture.Host.Scenario(s =>
            {
                s.WithClaim(new Claim("username", "bob"));
                s.Delete.Url("/api/chat/blocks/alice");
                s.StatusCodeShouldBe(HttpStatusCode.NoContent);
            }));
        Assert.Single(unblocked.Sent.MessagesOf<UserUnblocked>());
        await Message(chat.Id, "alice", "Thanks!");
    }

    [Fact]
    public async Task Report_SpamAbuseScamOrOther_ForTheAdmins()
    {
        var chat = await Start("alice");
        await Message(chat.Id, "alice", "Pay me outside the app");

        await SendText(s => s.Post.Json(new ReportDto { Reason = "rude" }).ToUrl($"/api/chat/conversations/{chat.Id}/report"), "bob", HttpStatusCode.BadRequest);
        await SendText(s => s.Post.Json(new ReportDto { Reason = "scam", Details = "Asks for payment by bank transfer" })
            .ToUrl($"/api/chat/conversations/{chat.Id}/report"), "bob", HttpStatusCode.NoContent);

        await SendText(s => s.Get.Url("/api/chat/admin/reports"), "bob", HttpStatusCode.Forbidden);
        var reports = await SendText(s => s.Get.Url("/api/chat/admin/reports"), "admin", HttpStatusCode.OK);
        Assert.Contains("\"reportedUser\":\"alice\"", reports);
        Assert.Contains("Pay me outside the app", reports);
    }

    [Fact]
    public async Task RateLimit_TooManyMessagesAMinute()
    {
        var chat = await Start("alice");
        for (var i = 0; i < 5; i++) await Message(chat.Id, "alice", $"Message {i}");

        var limited = await SendText(s => s.Post.Json(new SendMessageDto { Text = "one more" }).ToUrl($"/api/chat/conversations/{chat.Id}/messages"),
            "alice", HttpStatusCode.TooManyRequests);
        Assert.Contains("too fast", limited);
    }

    [Fact]
    public async Task DeletedListing_ChatsStayReadable_ButReadOnly()
    {
        var chat = await Start("alice");
        await Message(chat.Id, "alice", "Is this still available?");

        await fixture.Host.InvokeMessageAndWaitAsync(new AuctionDeleted { Id = _listing });

        var view = await Send<ConversationDto>(s => s.Get.Url($"/api/chat/conversations/{chat.Id}"), "alice");
        Assert.True(view.ReadOnly);
        Assert.Single(view.Messages);
        await SendText(s => s.Post.Json(new SendMessageDto { Text = "Hello?" }).ToUrl($"/api/chat/conversations/{chat.Id}/messages"), "bob", HttpStatusCode.BadRequest);
        await SendText(s => s.Post.Json(new StartConversationDto { ListingId = _listing }).ToUrl("/api/chat/conversations"), "carol", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AfterTheSale_TheSellerCanMessageTheWinner_AndTheHeaderFollowsBidsAndTheEnd()
    {
        await Start("carol"); // the chat now knows the listing
        await fixture.Host.InvokeMessageAndWaitAsync(new BidPlaced { Id = "b1", AuctionId = _listing, Bidder = "alice", Amount = 14500, BidStatus = "Accepted" });
        await fixture.Host.InvokeMessageAndWaitAsync(new AuctionFinished { AuctionId = _listing, Seller = "bob", Winner = "alice", Amount = 14500, ItemSold = true, Status = "Sold" });
        await fixture.Host.InvokeMessageAndWaitAsync(new OrderCreated { OrderId = "o1", AuctionId = _listing, Seller = "bob", Buyer = "alice" });

        // "Message buyer" on the order: the seller may start this one.
        var chat = await Start("bob", buyer: "alice");
        Assert.Equal(("Sold", 14500), (chat.Listing.Status, chat.Listing.Price));
        Assert.False(chat.ShowSafetyTip);
        await Message(chat.Id, "bob", "Thanks! When can you pick it up?");
        Assert.Equal("Thanks! When can you pick it up?", (await Inbox("alice")).Single().LastMessage);
    }

    [Fact]
    public async Task OlderMessages_LoadPageByPage()
    {
        var chat = await Start("alice");
        // Seed 60 messages directly (the rate limit is 5 a minute in the tests).
        using (var scope = fixture.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
            var start = DateTime.UtcNow.AddHours(-2);
            for (var i = 0; i < 60; i++)
                db.Messages.Add(new Entities.Message { ConversationId = chat.Id, Sender = i % 2 == 0 ? "alice" : "bob", Text = $"m{i}", SentAt = start.AddMinutes(i) });
            await db.SaveChangesAsync();
        }

        var latest = await Send<ConversationDto>(s => s.Get.Url($"/api/chat/conversations/{chat.Id}"), "alice");
        Assert.Equal(50, latest.Messages.Count);
        Assert.Equal(("m10", "m59"), (latest.Messages[0].Text, latest.Messages[^1].Text));
        Assert.True(latest.HasOlder);

        var before = Uri.EscapeDataString(latest.Messages[0].SentAt.ToString("o"));
        var older = await Send<ConversationDto>(s => s.Get.Url($"/api/chat/conversations/{chat.Id}?before={before}"), "alice");
        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"m{i}"), older.Messages.Select(m => m.Text));
        Assert.False(older.HasOlder);
    }

    [Fact]
    public async Task TheOtherPersonsRating_ShowsInTheInboxAndTheChat()
    {
        await fixture.Host.InvokeMessageAndWaitAsync(new SellerRatingChanged { Seller = "bob", Average = 4.7, Count = 32 });
        var chat = await Start("alice");
        await Message(chat.Id, "alice", "Hello");

        Assert.Equal((4.7, 32), ((await Inbox("alice")).Single().OtherUserRating, (await Inbox("alice")).Single().OtherUserRatingCount));
        var view = await Send<ConversationDto>(s => s.Get.Url($"/api/chat/conversations/{chat.Id}"), "alice");
        Assert.Equal((4.7, 32), (view.OtherUserRating, view.OtherUserRatingCount));
        // alice has no seller ratings.
        Assert.Null((await Inbox("bob")).Single().OtherUserRating);
    }

    [Fact]
    public async Task ReviewReminder_IsPushedToTheBuyersPhones()
    {
        await SendText(s => s.Post.Json(new DeviceDto { Token = "alice-phone" }).ToUrl("/api/chat/devices"), "alice", HttpStatusCode.NoContent);

        await fixture.Host.InvokeMessageAndWaitAsync(new ReviewRequested { OrderId = "o1", AuctionId = _listing, Title = "Dash Cam", Buyer = "alice", Seller = "bob" });

        var push = fixture.Push.Sent.Last(p => p.Tokens.Contains("alice-phone"));
        Assert.Equal(("How was your purchase?", "review", "o1"), (push.Message.Title, push.Message.Data["type"], push.Message.Data["orderId"]));
    }

    private record UnreadDto(int Count);
}
