using BidService.Data;
using BidService.Endpoints;
using BidService.Services;
using Contracts;
using Mapster;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;
using Npgsql;
using Wolverine;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

TypeAdapterConfig.GlobalSettings.Scan(typeof(Program).Assembly);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["IdentityServiceUrl"];
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters.ValidateAudience = false;
        options.TokenValidationParameters.NameClaimType = "username";
    });

var connString = builder.Configuration.GetConnectionString("BidDbConnection");
if (string.IsNullOrWhiteSpace(connString)) throw new ArgumentException("Connection string is empty");

builder.Services.AddSingleton(NpgsqlDataSource.Create(connString));
builder.Services.AddSingleton<BidDbContext>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<AntiSnipingOptions>(builder.Configuration.GetSection(AntiSnipingOptions.SectionName));
builder.Services.Configure<AuctionRulesOptions>(builder.Configuration.GetSection(AuctionRulesOptions.SectionName));

builder.Host.UseWolverine(opts =>
{
    opts.PersistMessagesWithPostgresql(connString, "bids");
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
    opts.Policies.AlwaysMakeScheduledMessagesDurable();
    
    opts.UseRabbitMq(rabbit =>
        {
            rabbit.HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
            rabbit.UserName = builder.Configuration["RabbitMQ:Username"] ?? "guest";
            rabbit.Password = builder.Configuration["RabbitMQ:Password"] ?? "guest";
        })
        .DeclareExchange("auction-created", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("auction-updated", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("auction-deleted", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("bid-placed", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("auction-finished", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("auction-extended", ex => ex.ExchangeType = ExchangeType.Fanout)
        .BindExchange("auction-created").ToQueue("bid-auction-created")
        .BindExchange("auction-updated").ToQueue("bid-auction-updated")
        .BindExchange("auction-deleted").ToQueue("bid-auction-deleted")
        .AutoProvision();
    
    opts.ListenToRabbitQueue("bid-auction-created");
    opts.ListenToRabbitQueue("bid-auction-updated");
    opts.ListenToRabbitQueue("bid-auction-deleted");
    opts.PublishMessage<BidPlaced>().ToRabbitExchange("bid-placed");
    opts.PublishMessage<AuctionFinished>().ToRabbitExchange("auction-finished");
    opts.PublishMessage<AuctionExtended>().ToRabbitExchange("auction-extended");
});

builder.Services.AddScoped<IBidRepository, BidRepository>();
builder.Services.AddScoped<GrpcAuctionClient>();
builder.Services.AddScoped<BiddingService>();

builder.Services.AddAuthorization();

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

var app = builder.Build();

// Configure the HTTP request pipeline.

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapBidEndpoints();

try
{
    DbInitializer.InitDb(app);
}
catch (Exception e)
{
    Console.WriteLine($"Error initializing bid db: {e.Message}");
}

app.Run();

public partial class Program { }