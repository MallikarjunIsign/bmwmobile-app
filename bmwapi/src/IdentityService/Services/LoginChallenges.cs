using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace IdentityService.Services;

/// <summary>
/// The "challenge id" the app gets after email + password when a code is needed.
/// It is the user id, encrypted and valid for 15 minutes, so the app never sees or sends raw user ids
/// and an old challenge stops working by itself.
/// </summary>
public interface ILoginChallenges
{
    string Create(string userId);

    /// <summary>The user id, or null when the challenge is invalid or older than 15 minutes.</summary>
    string? Read(string? challengeId);
}

public class LoginChallenges(IDataProtectionProvider provider) : ILoginChallenges
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly ITimeLimitedDataProtector _protector =
        provider.CreateProtector("IdentityService.LoginChallenge.v1").ToTimeLimitedDataProtector();

    public string Create(string userId) => _protector.Protect(userId, Lifetime);

    public string? Read(string? challengeId)
    {
        if (string.IsNullOrWhiteSpace(challengeId)) return null;
        try
        {
            return _protector.Unprotect(challengeId);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
