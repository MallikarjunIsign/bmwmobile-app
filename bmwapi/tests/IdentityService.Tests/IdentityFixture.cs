using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Alba;
using Duende.IdentityServer.Configuration;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace IdentityService.Tests;

/// <summary>Emails are not sent in tests: the codes are kept here so a test can "read the email".</summary>
public class CapturingOtpSender : IOtpSender
{
    private readonly ConcurrentDictionary<(string Email, OtpPurpose Purpose), string> _codes = new();

    public Task SendAsync(ApplicationUser user, string code, OtpPurpose purpose, CancellationToken ct = default)
    {
        _codes[(user.Email!, purpose)] = code;
        return Task.CompletedTask;
    }

    public string CodeFor(string email, OtpPurpose purpose) => _codes[(email, purpose)];
}

/// <summary>Notice emails (e.g. "New seller application" to the admins) kept for the tests to read.</summary>
public class CapturingMailSender : IMailSender
{
    public ConcurrentQueue<(IReadOnlyList<MailRecipient> To, string Subject, string Text)> Sent { get; } = new();

    public Task SendAsync(IReadOnlyList<MailRecipient> to, string subject, string text, string html, CancellationToken ct = default)
    {
        Sent.Enqueue((to, subject, text));
        return Task.CompletedTask;
    }
}

/// <summary>
/// The real Identity service (IdentityServer, token endpoint, account + admin API) on a Postgres test container.
/// Tokens are real: the tests sign in like the app (bmw_password + bmw_otp) or like Bruno (password grant).
/// </summary>
public class IdentityFixture : IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Adm1n!Passw0rd";
    public const string SellerEmail = "seller@test.local";
    public const string SellerPassword = "Sell3r!Passw0rd";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("identity").WithUsername("postgres").WithPassword("postgrespw").Build();

    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), $"identity-test-keys-{Guid.NewGuid():N}");

    public IAlbaHost Host { get; private set; } = null!;
    public CapturingOtpSender Emails { get; } = new();
    public CapturingMailSender Mail { get; } = new();
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
            builder.UseSetting("Seed:AdminEmail", AdminEmail);
            builder.UseSetting("Seed:AdminPassword", AdminPassword);
            builder.UseSetting("Seed:SellerEmail", SellerEmail);
            builder.UseSetting("Seed:SellerPassword", SellerPassword);
            builder.UseSetting("RoleBackfill:AuctionServiceUrl", "");
            // Every test calls from the same address: the per-IP limits are for real phones, not this test run.
            builder.UseSetting("RateLimits:AccountPerMinute", "1000");
            builder.UseSetting("RateLimits:SellerApplicationsPerMinute", "1000");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOtpSender>();
                services.AddSingleton<IOtpSender>(Emails);
                services.RemoveAll<IMailSender>();
                services.AddSingleton<IMailSender>(Mail);
                // Signing keys go to a temp folder, not the service's own keys folder.
                services.PostConfigure<IdentityServerOptions>(o => o.KeyManagement.KeyPath = _keyPath);
            });
        });

        // The entry point stops before SeedData when hosted by the tests, so run it here.
        SeedData.EnsureSeedData(Host.Services, Host.Services.GetRequiredService<IConfiguration>());
        Client = Host.Server.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Host.StopAsync();
        await Host.DisposeAsync();
        await _postgres.DisposeAsync();
        if (Directory.Exists(_keyPath)) Directory.Delete(_keyPath, recursive: true);
    }

    // ── token helpers ──────────────────────────────────────────────────────

    public record TokenResult(int Status, JsonElement Body)
    {
        public string AccessToken => Body.GetProperty("access_token").GetString()!;
        public string? RefreshToken => Body.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        public string? Code => Body.TryGetProperty("code", out var c) ? c.GetString() : null;
    }

    public async Task<TokenResult> TokenAsync(Dictionary<string, string> form)
    {
        using var response = await Client.PostAsync("/connect/token", new FormUrlEncodedContent(form));
        return new TokenResult((int)response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    /// <summary>Signs in like the app: email + password, then the emailed code. Returns access + refresh token.</summary>
    public async Task<TokenResult> AppSignInAsync(string email, string password)
    {
        const string scope = "openid profile auctionApp IdentityServerApi offline_access";
        var first = await TokenAsync(new()
        {
            ["grant_type"] = "bmw_password", ["client_id"] = "mobileApp", ["scope"] = scope,
            ["username"] = email, ["password"] = password, ["device_id"] = "test-phone",
        });
        if (first.Code != "OTP_REQUIRED") return first;

        return await TokenAsync(new()
        {
            ["grant_type"] = "bmw_otp", ["client_id"] = "mobileApp", ["scope"] = scope,
            ["challenge_id"] = first.Body.GetProperty("challengeId").GetString()!,
            ["otp"] = Emails.CodeFor(email, OtpPurpose.Login), ["device_id"] = "test-phone",
        });
    }

    public Task<TokenResult> RefreshAsync(string refreshToken) => TokenAsync(new()
    {
        ["grant_type"] = "refresh_token", ["client_id"] = "mobileApp", ["refresh_token"] = refreshToken,
    });

    /// <summary>The claims inside a JWT access token (no validation: the service issued it).</summary>
    public static JsonElement Claims(string accessToken)
    {
        var payload = accessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null) request.Headers.Authorization = new("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await Client.SendAsync(request);
    }
}

[CollectionDefinition("identity-service")]
public class IdentityServiceCollection : ICollectionFixture<IdentityFixture>;
