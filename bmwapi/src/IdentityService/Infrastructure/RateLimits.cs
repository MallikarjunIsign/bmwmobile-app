using System.Threading.RateLimiting;
using IdentityService.Contracts;

namespace IdentityService.Infrastructure;

public static class RateLimits
{
    public const string Account = "account";

    /// <summary>Basic brute-force protection for /api/account: 20 requests per minute per IP address.</summary>
    public static IServiceCollection AddAccountRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
                await ctx.HttpContext.Response.WriteAsJsonAsync(
                    new ErrorResponse("Too many attempts. Please wait a minute and try again.", "RATE_LIMITED"), ct);
            o.AddPolicy(Account, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
}
