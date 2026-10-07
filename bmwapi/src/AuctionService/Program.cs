using AuctionService.Data;
using System.Text.Json.Serialization;
using AuctionService.Errors;
using AuctionService.RequestHelpers;
using AuctionService.Services;
using AuctionService.Validation;
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
    // case "AzureBlob": builder.Services.AddSingleton<IImageStorage, AzureBlobImageStorage>(); break;
    default:
        throw new Exception($"Unknown ImageStorage:Provider '{imageStorageProvider}'");
}
builder.Services.AddSingleton<AuctionMapper>();
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
        .BindExchange("auction-finished").ToQueue("auction-auction-finished")
        .BindExchange("bid-placed").ToQueue("auction-bid-placed")
        .BindExchange("auction-extended").ToQueue("auction-auction-extended")
        // BidService's queues are declared here too, so auction events published before BidService has ever
        // started (e.g. the seed data on a fresh install) are kept for it instead of being dropped.
        .BindExchange("auction-created").ToQueue("bid-auction-created")
        .BindExchange("auction-updated").ToQueue("bid-auction-updated")
        .BindExchange("auction-deleted").ToQueue("bid-auction-deleted")
        .AutoProvision();
    
    opts.PublishMessage<AuctionCreated>().ToRabbitExchange("auction-created");
    opts.PublishMessage<AuctionUpdated>().ToRabbitExchange("auction-updated");
    opts.PublishMessage<AuctionDeleted>().ToRabbitExchange("auction-deleted");
    opts.PublishMessage<ContainerUpdated>().ToRabbitExchange("container-updated");
    
    opts.ListenToRabbitQueue("wolverine-dead-letter-queue");
    opts.ListenToRabbitQueue("auction-auction-finished");
    opts.ListenToRabbitQueue("auction-bid-placed");
    opts.ListenToRabbitQueue("auction-auction-extended");
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