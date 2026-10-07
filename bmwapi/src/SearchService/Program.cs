using Mapster;
using Meilisearch;
using SearchService.Data;
using SearchService.Endpoints;
using SearchService.Handlers;
using SearchService.Services;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;

TypeAdapterConfig.GlobalSettings.Scan(typeof(Program).Assembly);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();

    return new MeilisearchClient(
        config["Meilisearch:Url"],
        config["Meilisearch:ApiKey"]);
});
builder.Services.AddHttpClient<AuctionSvcHttpClient>()
    .AddStandardResilienceHandler(options =>
    {
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(3);
        options.Retry.MaxRetryAttempts = 5;
        options.Retry.Delay = TimeSpan.FromSeconds(10);
        options.Retry.OnRetry = args =>
        {
            Console.WriteLine(
                $"Auction svc unavailable.  Retry {args.AttemptNumber + 1}/5");
            return default;
        };
    });
builder.Host.UseWolverine(opts =>
{
    opts.UseRabbitMq(rabbit =>
        {
            rabbit.HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
            rabbit.UserName = builder.Configuration["RabbitMQ:Username"] ?? "guest";
            rabbit.Password = builder.Configuration["RabbitMQ:Password"] ?? "guest";
        })
        .DeclareExchange("auction-created", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("auction-updated", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("auction-deleted", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("auction-finished", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("bid-placed", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("auction-extended", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("container-updated", ex => ex.ExchangeType = ExchangeType.Fanout)
        .BindExchange("auction-created").ToQueue("search-auction-created")
        .BindExchange("auction-updated").ToQueue("search-auction-updated")
        .BindExchange("auction-deleted").ToQueue("search-auction-deleted")
        .BindExchange("auction-finished").ToQueue("search-auction-finished")
        .BindExchange("bid-placed").ToQueue("search-bid-placed")
        .BindExchange("auction-extended").ToQueue("search-auction-extended")
        .BindExchange("container-updated").ToQueue("search-container-updated")
        .AutoProvision();
    
    opts.ListenToRabbitQueue("search-auction-created");
    opts.ListenToRabbitQueue("search-auction-updated");
    opts.ListenToRabbitQueue("search-auction-deleted");
    opts.ListenToRabbitQueue("search-auction-finished");
    opts.ListenToRabbitQueue("search-bid-placed");
    opts.ListenToRabbitQueue("search-auction-extended");
    opts.ListenToRabbitQueue("search-container-updated");

    opts.OnException<TransientSearchException>()
        .RetryWithCooldown(
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(1)).Then.MoveToErrorQueue();

    // Events arrive on separate queues, so a bid/end/extension can be handled before the auction's
    // AuctionCreated has been indexed. Retry for a while instead of dead-lettering it straight away.
    opts.OnException<MeilisearchApiError>(e => e.Code == "document_not_found")
        .RetryWithCooldown(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10)).Then.MoveToErrorQueue();
});

var app = builder.Build();

// Configure the HTTP request pipeline.

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/api/search", SearchEndpoints.GetSearchResults);
app.MapGet("/api/search/{id}", SearchEndpoints.GetAuctionById);

try
{
    await DbInitializer.ConfigureIndex(app);
}
catch (Exception e)
{
    Console.WriteLine($"Failed to seed search:  {e.Message}");
}

if (!app.Environment.IsEnvironment("Test"))
{
    _ = Task.Run(async () =>
    {
        try
        {
            await DbInitializer.FetchMissingAuctions(app);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to seed search: {e.Message}");
        }
    });
}

app.Run();

public partial class Program { }