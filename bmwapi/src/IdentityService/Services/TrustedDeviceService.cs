using System.Security.Cryptography;
using System.Text;
using IdentityService.Data;
using IdentityService.Models;
using IdentityService.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IdentityService.Services;

/// <summary>
/// "Remember this device" (skip the email code for 30 days).
/// The phone gets a random device token from the server; the database keeps only its SHA-256 hash,
/// so a leaked database or a guessed device ID can't be used to skip the code.
/// </summary>
public interface ITrustedDeviceService
{
    /// <summary>Trusts this device and returns the new device token for the phone to keep.</summary>
    Task<string> TrustAsync(string userId, string deviceId, CancellationToken ct = default);

    Task<bool> IsTrustedAsync(string userId, string? deviceId, string? deviceToken, CancellationToken ct = default);

    Task ForgetAllAsync(string userId, CancellationToken ct = default);
}

public class TrustedDeviceService(ApplicationDbContext db, IOptions<TrustedDeviceOptions> options) : ITrustedDeviceService
{
    public async Task<string> TrustAsync(string userId, string deviceId, CancellationToken ct = default)
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = DateTime.UtcNow;
        var expires = now.AddDays(options.Value.Days);

        var device = await db.TrustedDevices.FirstOrDefaultAsync(d => d.UserId == userId && d.DeviceId == deviceId, ct);
        if (device is null)
        {
            db.TrustedDevices.Add(new TrustedDevice
            {
                UserId = userId,
                DeviceId = deviceId,
                TokenHash = Hash(token),
                CreatedAt = now,
                ExpiresAt = expires,
            });
        }
        else
        {
            device.TokenHash = Hash(token);
            device.CreatedAt = now;
            device.ExpiresAt = expires;
        }

        await db.SaveChangesAsync(ct);
        return token;
    }

    public async Task<bool> IsTrustedAsync(string userId, string? deviceId, string? deviceToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(deviceToken)) return false;

        var device = await db.TrustedDevices.FirstOrDefaultAsync(d => d.UserId == userId && d.DeviceId == deviceId, ct);
        if (device is null || device.ExpiresAt <= DateTime.UtcNow) return false;

        var ok = CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(device.TokenHash),
            Convert.FromHexString(Hash(deviceToken)));
        if (!ok) return false;

        device.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ForgetAllAsync(string userId, CancellationToken ct = default)
    {
        var devices = await db.TrustedDevices.Where(d => d.UserId == userId).ToListAsync(ct);
        db.TrustedDevices.RemoveRange(devices);
        await db.SaveChangesAsync(ct);
    }

    internal static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
