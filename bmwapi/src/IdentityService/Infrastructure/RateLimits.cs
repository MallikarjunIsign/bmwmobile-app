using System.Threading.RateLimiting;
using IdentityService.Contracts;

namespace IdentityService.Infrastructure;

public static class RateLimits
{
    public const string Account = "account";
    public const string SellerApplications = "seller-applications";

    /// <summary>
    /// Basic brute-force / spam protection per IP address: /api/account 20 requests per minute (RateLimits:AccountPerMinute),
    /// the "Want to sell?" form 5 per minute (RateLimits:SellerApplicationsPerMinute).
    /// </summary>
    public static IServiceCollection AddAccountRateLimits(this IServiceCollection services, IConfiguration config) =>
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
                await ctx.HttpContext.Response.WriteAsJsonAsync(
                    new ErrorResponse("Too many attempts. Please wait a minute and try again.", "RATE_LIMITED"), ct);
            o.AddPolicy(Account, http => PerIp(http, config.GetValue("RateLimits:AccountPerMinute", 20)));
            o.AddPolicy(SellerApplications, http => PerIp(http, config.GetValue("RateLimits:SellerApplicationsPerMinute", 5)));
        });

    private static RateLimitPartition<string> PerIp(HttpContext http, int perMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
}
