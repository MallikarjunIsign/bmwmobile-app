using System.Globalization;
using System.Text;
using IdentityService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Services;

/// <summary>
/// Username = first name, lowercase, letters and numbers only (e.g. "Mallikarjun Reddy" → "mallikarjun").
/// If it is taken a number is added: mallikarjun2, mallikarjun3, ...
/// The username is the public name on auctions and bids; people log in with their email.
/// </summary>
public static class UsernameGenerator
{
    private const int MaxBaseLength = 20;

    public static string BaseFromFullName(string? fullName)
    {
        var first = (fullName ?? string.Empty).Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

        // "José" → "jose": drop accents, then keep ASCII letters and digits only.
        var sb = new StringBuilder();
        foreach (var c in first.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            var lower = char.ToLowerInvariant(c);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(lower);
        }

        var name = sb.ToString();
        if (name.Length > MaxBaseLength) name = name[..MaxBaseLength];
        return name.Length == 0 ? "user" : name;
    }

    /// <summary>The first free name: base, base2, base3, ... given the names already taken.</summary>
    public static string FirstFree(string baseName, IEnumerable<string> taken)
    {
        var set = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(baseName)) return baseName;
        for (var n = 2; ; n++)
        {
            var candidate = $"{baseName}{n}";
            if (!set.Contains(candidate)) return candidate;
        }
    }

    public static async Task<string> NextAvailableAsync(UserManager<ApplicationUser> users, string? fullName, CancellationToken ct)
    {
        var baseName = BaseFromFullName(fullName);
        var prefix = baseName.ToUpperInvariant();
        var taken = await users.Users
            .Where(u => u.NormalizedUserName != null && u.NormalizedUserName.StartsWith(prefix))
            .Select(u => u.UserName!)
            .ToListAsync(ct);
        return FirstFree(baseName, taken);
    }
}
