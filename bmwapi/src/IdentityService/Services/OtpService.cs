using System.Security.Cryptography;
using System.Text;
using IdentityService.Data;
using IdentityService.Infrastructure;
using IdentityService.Models;
using IdentityService.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IdentityService.Services;

/// <summary>Result of asking for a code. <see cref="Code"/> is null when a recent code is still waiting (cooldown).</summary>
public record OtpIssueResult(string? Code, int ResendAfterSeconds);

public interface IOtpService
{
    /// <summary>
    /// Creates and emails a new code (older ones stop working).
    /// Within the resend cooldown it throws, unless <paramref name="reuseRecent"/> is true:
    /// then the recent code stays valid and the remaining wait is returned.
    /// </summary>
    Task<OtpIssueResult> IssueAsync(ApplicationUser user, OtpPurpose purpose, bool reuseRecent = false, CancellationToken ct = default);

    /// <summary>Throws <see cref="AppException"/> with a user-friendly message when the code is wrong or expired.</summary>
    Task VerifyAsync(ApplicationUser user, OtpPurpose purpose, string code, CancellationToken ct = default);

    int ResendCooldownSeconds { get; }

    /// <summary>Of the given purposes, the one with the newest unused, unexpired code (null = none).</summary>
    Task<OtpPurpose?> NewestActiveAsync(ApplicationUser user, OtpPurpose[] purposes, CancellationToken ct = default);
}

public class OtpService(ApplicationDbContext db, IOtpSender sender, IOptions<OtpOptions> options) : IOtpService
{
    private readonly OtpOptions _opt = options.Value;

    public int ResendCooldownSeconds => _opt.ResendCooldownSeconds;

    public async Task<OtpIssueResult> IssueAsync(ApplicationUser user, OtpPurpose purpose, bool reuseRecent = false, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var active = await db.OtpCodes
            .Where(o => o.UserId == user.Id && o.Purpose == purpose && o.UsedAt == null)
            .ToListAsync(ct);

        var latest = active.OrderByDescending(o => o.CreatedAt).FirstOrDefault();
        if (latest is not null && latest.ExpiresAt > now)
        {
            var waitUntil = latest.CreatedAt.AddSeconds(_opt.ResendCooldownSeconds);
            if (waitUntil > now)
            {
                var seconds = (int)Math.Ceiling((waitUntil - now).TotalSeconds);
                if (reuseRecent) return new OtpIssueResult(null, seconds);
                throw AppException.TooMany($"Please wait {seconds} seconds before requesting a new code.", "OTP_COOLDOWN");
            }
        }

        // Only the newest code is valid.
        foreach (var old in active) old.UsedAt = now;

        var code = GenerateCode(_opt.Length);
        db.OtpCodes.Add(new OtpCode
        {
            UserId = user.Id,
            Purpose = purpose,
            CodeHash = Hash(user.Id, code),
            CreatedAt = now,
            ExpiresAt = purpose == OtpPurpose.SellerInvite ? now.AddHours(_opt.InviteExpiryHours) : now.AddMinutes(_opt.ExpiryMinutes),
        });
        await db.SaveChangesAsync(ct);

        await sender.SendAsync(user, code, purpose, ct);
        return new OtpIssueResult(code, _opt.ResendCooldownSeconds);
    }

    public async Task VerifyAsync(ApplicationUser user, OtpPurpose purpose, string code, CancellationToken ct = default)
    {
        var otp = await db.OtpCodes
            .Where(o => o.UserId == user.Id && o.Purpose == purpose && o.UsedAt == null)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (otp is null || otp.ExpiresAt < DateTime.UtcNow)
            throw AppException.BadRequest("This code has expired. Tap Resend to get a new one.", "OTP_EXPIRED");

        if (otp.Attempts >= _opt.MaxAttempts)
            throw AppException.TooMany("Too many wrong tries. Tap Resend to get a new code.", "OTP_TOO_MANY_ATTEMPTS");

        var expected = Convert.FromHexString(otp.CodeHash);
        var actual = Convert.FromHexString(Hash(user.Id, (code ?? string.Empty).Trim()));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            otp.Attempts++;
            await db.SaveChangesAsync(ct);
            var left = _opt.MaxAttempts - otp.Attempts;
            throw AppException.BadRequest(
                left > 0 ? $"Wrong code, try again. {left} {(left == 1 ? "try" : "tries")} left." : "Too many wrong tries. Tap Resend to get a new code.",
                left > 0 ? "OTP_INVALID" : "OTP_TOO_MANY_ATTEMPTS");
        }

        otp.UsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<OtpPurpose?> NewestActiveAsync(ApplicationUser user, OtpPurpose[] purposes, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var newest = await db.OtpCodes
            .Where(o => o.UserId == user.Id && purposes.Contains(o.Purpose) && o.UsedAt == null && o.ExpiresAt > now)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return newest?.Purpose;
    }

    private static string GenerateCode(int length)
    {
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++) sb.Append(RandomNumberGenerator.GetInt32(0, 10));
        return sb.ToString();
    }

    internal static string Hash(string userId, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{code}")));
}
