using NotificationService.Hubs;
using Wolverine;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

builder.Host.UseWolverine(opts =>
{
    opts.UseRabbitMq(rabbit =>
        {
            rabbit.HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
            rabbit.UserName = builder.Configuration["RabbitMQ:Username"] ?? "guest";
            rabbit.Password = builder.Configuration["RabbitMQ:Password"] ?? "guest";
        })
        .DeclareExchange("auction-created", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("auction-finished", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("bid-placed", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("auction-extended", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("container-updated", ex => ex.ExchangeType = ExchangeType.Fanout)
        .BindExchange("auction-created").ToQueue("notification-auction-created")
        .BindExchange("auction-finished").ToQueue("notification-auction-finished")
        .BindExchange("bid-placed").ToQueue("notification-bid-placed")
        .BindExchange("auction-extended").ToQueue("notification-auction-extended")
        .BindExchange("container-updated").ToQueue("notification-container-updated")
        .AutoProvision();
    
    opts.ListenToRabbitQueue("notification-auction-created");
    opts.ListenToRabbitQueue("notification-auction-finished");
    opts.ListenToRabbitQueue("notification-bid-placed");
    opts.ListenToRabbitQueue("notification-auction-extended");
    opts.ListenToRabbitQueue("notification-container-updated");
});

var app = builder.Build();

app.MapHub<NotificationHub>("/notifications");

app.Run();