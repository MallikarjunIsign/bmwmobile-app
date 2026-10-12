using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace ChatService.Services;

/// <summary>A notification for one phone. Data is passed to the app (e.g. which conversation to open).</summary>
public record PushMessage(string Title, string Body, IReadOnlyDictionary<string, string> Data);

public interface IPushSender
{
    /// <returns>The tokens that are no longer valid (the app was uninstalled / signed out): delete them.</returns>
    Task<IReadOnlyList<string>> SendAsync(IReadOnlyList<string> tokens, PushMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Push:Provider = None (default until Firebase is set up): nothing is sent, the app still gets live messages.</summary>
public class NoPushSender(ILogger<NoPushSender> logger) : IPushSender
{
    public Task<IReadOnlyList<string>> SendAsync(IReadOnlyList<string> tokens, PushMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Push is off; not sending \"{Title}\" to {Count} device(s)", message.Title, tokens.Count);
        return Task.FromResult<IReadOnlyList<string>>([]);
    }
}

/// <summary>
/// Push:Provider = Firebase: Firebase Cloud Messaging. The service-account key file comes from
/// Push:Firebase:CredentialsFile (user-secrets / environment) or GOOGLE_APPLICATION_CREDENTIALS – never the repo.
/// </summary>
public class FirebasePushSender : IPushSender
{
    private readonly FirebaseMessaging _messaging;
    private readonly ILogger<FirebasePushSender> _logger;

    public FirebasePushSender(IConfiguration configuration, ILogger<FirebasePushSender> logger)
    {
        _logger = logger;
        var file = configuration["Push:Firebase:CredentialsFile"];
        var credential = string.IsNullOrWhiteSpace(file) ? GoogleCredential.GetApplicationDefault() : GoogleCredential.FromFile(file);
        var app = FirebaseApp.DefaultInstance ?? FirebaseApp.Create(new AppOptions { Credential = credential });
        _messaging = FirebaseMessaging.GetMessaging(app);
    }

    public async Task<IReadOnlyList<string>> SendAsync(IReadOnlyList<string> tokens, PushMessage message, CancellationToken cancellationToken = default)
    {
        if (tokens.Count == 0) return [];
        var response = await _messaging.SendEachForMulticastAsync(new MulticastMessage
        {
            Tokens = tokens,
            Notification = new Notification { Title = message.Title, Body = message.Body },
            Data = message.Data,
            Android = new AndroidConfig { Priority = Priority.High, Notification = new AndroidNotification { ChannelId = "chat" } },
            Apns = new ApnsConfig { Aps = new Aps { Sound = "default" } }
        }, cancellationToken);

        var invalid = new List<string>();
        for (var i = 0; i < response.Responses.Count; i++)
        {
            var result = response.Responses[i];
            if (result.IsSuccess) continue;
            if (result.Exception?.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument)
                invalid.Add(tokens[i]);
            else
                _logger.LogWarning(result.Exception, "Push to a device failed");
        }
        return invalid;
    }
}
