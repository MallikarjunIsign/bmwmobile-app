using AuctionService.Data;
using System.Text.Json.Serialization;
using AuctionService.Errors;
using AuctionService.RequestHelpers;
using AuctionService.Services;
using AuctionService.Services.Geocoding;
using AuctionService.Validation;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Contracts;
using Mapster;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

TypeAdapterConfig.GlobalSettings.Scan(typeof(Program).Assembly);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddGrpc();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste an access token from the IdentityService /connect/token endpoint"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var connString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connString)) throw new Exception("Connection string is empty");

builder.Services.AddDbContextWithWolverineIntegration<AuctionDbContext>(options =>
{
    options.UseNpgsql(connString);
    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});
builder.Services.Configure<ImageStorageOptions>(builder.Configuration.GetSection(ImageStorageOptions.SectionName));
builder.Services.Configure<ImageUploadOptions>(builder.Configuration.GetSection(ImageUploadOptions.SectionName));
// Config binding appends array items to the defaults, so normalize the list once.
builder.Services.PostConfigure<ImageUploadOptions>(o =>
    o.AllowedExtensions = o.AllowedExtensions.Select(x => x.ToLowerInvariant()).Distinct().ToArray());
builder.Services.AddSingleton<ImageUrlBuilder>();
var imageStorageProvider = builder.Configuration["ImageStorage:Provider"] ?? "Local";
switch (imageStorageProvider)
{
    case "Local":
        builder.Services.AddSingleton<IImageStorage, LocalDiskImageStorage>();
        break;
    case "GoogleCloudStorage":
        // Bucket from settings; credentials only from the environment (GOOGLE_APPLICATION_CREDENTIALS).
        builder.Services.AddSingleton<IImageStorage, GoogleCloudImageStorage>();
        break;
    default:
        throw new Exception($"Unknown ImageStorage:Provider '{imageStorageProvider}' (use Local or GoogleCloudStorage)");
}
builder.Services.AddScoped<ListingImageService>();
builder.Services.AddSingleton(TimeProvider.System);
// Reverse geocoding for the app's automatic location (city, country). Provider from settings; the Google API key
// only from environment variables / user-secrets.
builder.Services.Configure<GeocodingOptions>(builder.Configuration.GetSection(GeocodingOptions.SectionName));
builder.Services.AddMemoryCache();
var geocodingProvider = builder.Configuration["Geocoding:Provider"] ?? "BuiltIn";
switch (geocodingProvider)
{
    case "BuiltIn":
        builder.Services.AddSingleton<BuiltInReverseGeocoder>();
        builder.Services.AddSingleton<IReverseGeocoder>(sp => new CachedReverseGeocoder(
            sp.GetRequiredService<BuiltInReverseGeocoder>(), sp.GetRequiredService<IMemoryCache>(),
            sp.GetRequiredService<IOptions<GeocodingOptions>>()));
        break;
    case "Google":
        builder.Services.AddHttpClient<GoogleReverseGeocoder>(c => c.Timeout = TimeSpan.FromSeconds(10));
        builder.Services.AddSingleton<IReverseGeocoder>(sp => new CachedReverseGeocoder(
            sp.GetRequiredService<GoogleReverseGeocoder>(), sp.GetRequiredService<IMemoryCache>(),
            sp.GetRequiredService<IOptions<GeocodingOptions>>()));
        break;
    default:
        throw new Exception($"Unknown Geocoding:Provider '{geocodingProvider}' (use BuiltIn or Google)");
}
builder.Services.AddSingleton<AuctionMapper>();
builder.Services.AddScoped<CustomOrderMatcher>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<ShippingService>();
// US shipping: USPS Ground Advantage retail prices (Data/Seed/usps-ground-advantage-retail.csv).
builder.Services.AddSingleton(_ => UspsGroundAdvantage.FromFile());
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["IdentityServiceUrl"];
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters.ValidateAudience = false;
        options.TokenValidationParameters.NameClaimType = "username";
    });

builder.Services.AddAuthorization(options => options.AddContainerPolicy());

builder.Host.UseWolverine(opts =>
{
    opts.PersistMessagesWithPostgresql(connString, "auctions_rmq");
    opts.UseEntityFrameworkCoreTransactions();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
    
    opts.UseRabbitMq(rabbit =>
        {
            rabbit.HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
            rabbit.UserName = builder.Configuration["RabbitMQ:Username"] ?? "guest";
            rabbit.Password = builder.Configuration["RabbitMQ:Password"] ?? "guest";
        })
        .DeclareExchange("auction-created", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("auction-updated", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("auction-deleted", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("container-updated", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("auction-finished", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("bid-placed", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("auction-extended", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("custom-order-matched", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("order-status-changed", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("order-created", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("user-blocked", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("user-unblocked", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("seller-rating-changed", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("review-requested", ex => ex.ExchangeType = ExchangeType.Fanout)
        // A buyer's part request → every seller (live: NotificationService; push: ChatService).
        .DeclareExchange("part-request-created", ex => ex.ExchangeType = ExchangeType.Fanout)
        .BindExchange("part-request-created").ToQueue("notification-part-request-created")
        .BindExchange("part-request-created").ToQueue("chat-part-request-created")
        // A matching part was listed → the buyer's phones (push).
        .BindExchange("custom-order-matched").ToQueue("chat-custom-order-matched")
        // The consumers' queues are declared here too, so no rating change / reminder is lost while one is down.
        .BindExchange("seller-rating-changed").ToQueue("search-seller-rating-changed")
        .BindExchange("seller-rating-changed").ToQueue("chat-seller-rating-changed")
        .BindExchange("review-requested").ToQueue("notification-review-requested")
        .BindExchange("review-requested").ToQueue("chat-review-requested")
        .BindExchange("auction-finished").ToQueue("auction-auction-finished")
        .BindExchange("user-blocked").ToQueue("auction-user-blocked")
        .BindExchange("user-unblocked").ToQueue("auction-user-unblocked")
        // ChatService's queue is declared here too, so no sale is missed while it is down.
        .BindExchange("order-created").ToQueue("chat-order-created")
        .BindExchange("bid-placed").ToQueue("auction-bid-placed")
        .BindExchange("auction-extended").ToQueue("auction-auction-extended")
        // BidService's queues are declared here too, so auction events published before BidService has ever
        // started (e.g. the seed data on a fresh install) are kept for it instead of being dropped.
        .BindExchange("auction-created").ToQueue("bid-auction-created")
        .BindExchange("auction-updated").ToQueue("bid-auction-updated")
        .BindExchange("auction-deleted").ToQueue("bid-auction-deleted")
        // Kept for NotificationService even while it is down, so no "part now available" message is lost.
        .BindExchange("custom-order-matched").ToQueue("notification-custom-order-matched")
        .BindExchange("order-status-changed").ToQueue("notification-order-status-changed")
        .AutoProvision();
    
    opts.PublishMessage<AuctionCreated>().ToRabbitExchange("auction-created");
    opts.PublishMessage<AuctionUpdated>().ToRabbitExchange("auction-updated");
    opts.PublishMessage<AuctionDeleted>().ToRabbitExchange("auction-deleted");
    opts.PublishMessage<ContainerUpdated>().ToRabbitExchange("container-updated");
    opts.PublishMessage<CustomOrderMatched>().ToRabbitExchange("custom-order-matched");
    opts.PublishMessage<OrderStatusChanged>().ToRabbitExchange("order-status-changed");
    opts.PublishMessage<OrderCreated>().ToRabbitExchange("order-created");
    opts.PublishMessage<SellerRatingChanged>().ToRabbitExchange("seller-rating-changed");
    opts.PublishMessage<ReviewRequested>().ToRabbitExchange("review-requested");
    opts.PublishMessage<PartRequestCreated>().ToRabbitExchange("part-request-created");
    // "How was your purchase?" 2 days after completion: scheduled, stored in Postgres until then.
    opts.PublishMessage<AuctionService.Handlers.ReviewReminder>().ToLocalQueue("review-reminders");
    opts.LocalQueue("review-reminders").UseDurableInbox();
    
    opts.ListenToRabbitQueue("wolverine-dead-letter-queue");
    opts.ListenToRabbitQueue("auction-auction-finished");
    opts.ListenToRabbitQueue("auction-bid-placed");
    opts.ListenToRabbitQueue("auction-auction-extended");
    opts.ListenToRabbitQueue("auction-user-blocked");
    opts.ListenToRabbitQueue("auction-user-unblocked");
});

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (imageStorageProvider == "Local")
{
    app.UseLocalImageFiles();
}

app.MapControllers();
app.MapGrpcService<AuctionGrpcServer>();

try
{
    DbInitializer.InitDb(app);
}
catch (Exception e)
{
    Console.WriteLine($"Error initializing database: {e.Message}");
}

app.Run();

public partial class Program { }