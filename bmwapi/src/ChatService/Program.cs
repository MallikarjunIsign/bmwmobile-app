using ChatService.Data;
using ChatService.Services;
using Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var connString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connString)) throw new Exception("Connection string is empty");

builder.Services.AddDbContextWithWolverineIntegration<ChatDbContext>(options =>
{
    options.UseNpgsql(connString);
    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});
builder.Services.Configure<ChatOptions>(builder.Configuration.GetSection(ChatOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ListingDirectory>();
// Listings the chat hasn't heard about yet are fetched once from AuctionService (public GET /api/auctions/{id}).
builder.Services.AddHttpClient<IListingSource, HttpListingSource>(c =>
{
    c.BaseAddress = new Uri((builder.Configuration["AuctionServiceUrl"] ?? "http://localhost:7001").TrimEnd('/') + "/");
    c.Timeout = TimeSpan.FromSeconds(10);
});

// Push notifications for new messages: off until Firebase is configured (Push:Provider = Firebase + credentials).
var pushProvider = builder.Configuration["Push:Provider"] ?? "None";
switch (pushProvider)
{
    case "None":
        builder.Services.AddSingleton<IPushSender, NoPushSender>();
        break;
    case "Firebase":
        builder.Services.AddSingleton<IPushSender, FirebasePushSender>();
        break;
    default:
        throw new Exception($"Unknown Push:Provider '{pushProvider}' (use None or Firebase)");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["IdentityServiceUrl"];
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters.ValidateAudience = false;
        options.TokenValidationParameters.NameClaimType = "username";
    });
builder.Services.AddAuthorization();

builder.Host.UseWolverine(opts =>
{
    opts.PersistMessagesWithPostgresql(connString, "chat_rmq");
    opts.UseEntityFrameworkCoreTransactions();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
    opts.Policies.UseDurableLocalQueues();

    opts.UseRabbitMq(rabbit =>
        {
            rabbit.HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
            rabbit.UserName = builder.Configuration["RabbitMQ:Username"] ?? "guest";
            rabbit.Password = builder.Configuration["RabbitMQ:Password"] ?? "guest";
        })
        // Listings (header, buyers) from AuctionService and BidService.
        .DeclareExchange("auction-created", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("auction-updated", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("auction-deleted", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("auction-finished", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("bid-placed", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("order-created", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("seller-rating-changed", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("review-requested", ex => ex.ExchangeType = ExchangeType.Fanout)
        .BindExchange("seller-rating-changed").ToQueue("chat-seller-rating-changed")
        .BindExchange("review-requested").ToQueue("chat-review-requested")
        .BindExchange("auction-created").ToQueue("chat-auction-created")
        .BindExchange("auction-updated").ToQueue("chat-auction-updated")
        .BindExchange("auction-deleted").ToQueue("chat-auction-deleted")
        .BindExchange("auction-finished").ToQueue("chat-auction-finished")
        .BindExchange("bid-placed").ToQueue("chat-bid-placed")
        .BindExchange("order-created").ToQueue("chat-order-created")
        // What the chat publishes; the consumers' queues are declared here too so nothing is lost while one is down.
        .DeclareExchange("chat-message-sent", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("chat-receipt", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("user-blocked", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("user-unblocked", ex => ex.ExchangeType = ExchangeType.Fanout)
        .BindExchange("chat-message-sent").ToQueue("notification-chat-message-sent")
        .BindExchange("chat-receipt").ToQueue("notification-chat-receipt")
        .BindExchange("user-blocked").ToQueue("bid-user-blocked")
        .BindExchange("user-unblocked").ToQueue("bid-user-unblocked")
        .BindExchange("user-blocked").ToQueue("auction-user-blocked")
        .BindExchange("user-unblocked").ToQueue("auction-user-unblocked")
        .AutoProvision();

    opts.PublishMessage<ChatMessageSent>().ToRabbitExchange("chat-message-sent");
    opts.PublishMessage<ChatReceipt>().ToRabbitExchange("chat-receipt");
    opts.PublishMessage<UserBlocked>().ToRabbitExchange("user-blocked");
    opts.PublishMessage<UserUnblocked>().ToRabbitExchange("user-unblocked");
    // Pushes go through a durable local queue: sent after the message is committed, retried if Firebase is down.
    opts.PublishMessage<SendChatPush>().ToLocalQueue("chat-push");

    opts.ListenToRabbitQueue("chat-auction-created");
    opts.ListenToRabbitQueue("chat-auction-updated");
    opts.ListenToRabbitQueue("chat-auction-deleted");
    opts.ListenToRabbitQueue("chat-auction-finished");
    opts.ListenToRabbitQueue("chat-bid-placed");
    opts.ListenToRabbitQueue("chat-order-created");
    opts.ListenToRabbitQueue("chat-seller-rating-changed");
    opts.ListenToRabbitQueue("chat-review-requested");
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    try
    {
        scope.ServiceProvider.GetRequiredService<ChatDbContext>().Database.Migrate();
    }
    catch (Exception e)
    {
        Console.WriteLine($"Error initializing the chat database: {e.Message}");
    }
}

app.Run();

public partial class Program { }
