using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using NotificationService.Hubs;
using Wolverine;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, UsernameUserIdProvider>();
builder.Services.AddSingleton<NotificationService.Models.AuctionTitles>();

// Optional sign-in for the hub: the app passes its access token as ?access_token=... (WebSockets can't send headers),
// so personal messages (custom order matched) can be sent to that user only.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["IdentityServiceUrl"];
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters.ValidateAudience = false;
        options.TokenValidationParameters.NameClaimType = "username";
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/notifications"))
                    context.Token = token;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

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
        .DeclareExchange("custom-order-matched", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("order-status-changed", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("auction-ending-soon", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("chat-message-sent", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("chat-receipt", ex => ex.ExchangeType = ExchangeType.Fanout)
        .DeclareExchange("review-requested", ex => ex.ExchangeType = ExchangeType.Fanout)
        .BindExchange("auction-created").ToQueue("notification-auction-created")
        .BindExchange("auction-finished").ToQueue("notification-auction-finished")
        .BindExchange("bid-placed").ToQueue("notification-bid-placed")
        .BindExchange("auction-extended").ToQueue("notification-auction-extended")
        .BindExchange("container-updated").ToQueue("notification-container-updated")
        .BindExchange("custom-order-matched").ToQueue("notification-custom-order-matched")
        .BindExchange("order-status-changed").ToQueue("notification-order-status-changed")
        .BindExchange("auction-ending-soon").ToQueue("notification-auction-ending-soon")
        .BindExchange("chat-message-sent").ToQueue("notification-chat-message-sent")
        .BindExchange("chat-receipt").ToQueue("notification-chat-receipt")
        .BindExchange("review-requested").ToQueue("notification-review-requested")
        .AutoProvision();

    opts.ListenToRabbitQueue("notification-auction-created");
    opts.ListenToRabbitQueue("notification-auction-finished");
    opts.ListenToRabbitQueue("notification-bid-placed");
    opts.ListenToRabbitQueue("notification-auction-extended");
    opts.ListenToRabbitQueue("notification-container-updated");
    opts.ListenToRabbitQueue("notification-custom-order-matched");
    opts.ListenToRabbitQueue("notification-order-status-changed");
    opts.ListenToRabbitQueue("notification-auction-ending-soon");
    opts.ListenToRabbitQueue("notification-chat-message-sent");
    opts.ListenToRabbitQueue("notification-chat-receipt");
    opts.ListenToRabbitQueue("notification-review-requested");
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapHub<NotificationHub>("/notifications");

app.Run();
