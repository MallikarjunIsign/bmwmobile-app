using System.Text.RegularExpressions;

namespace ChatService.Services;

/// <summary>
/// Chat photos must have been uploaded through AuctionService (/api/media): a stored "/images/&lt;generated name&gt;"
/// file (whatever host the app reached it on) or a configured storage bucket. Anything else is refused, so a message
/// can't point at an arbitrary website.
/// </summary>
public static partial class PhotoUrls
{
    /// <summary>The value to store ("/images/x.jpg" or the storage URL), or null when the URL isn't one of ours.</summary>
    public static string? Normalize(string? url, IEnumerable<string> remotePrefixes)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 500) return null;
        url = url.Trim();

        if (remotePrefixes.Any(p => !string.IsNullOrEmpty(p) && url.StartsWith(p, StringComparison.OrdinalIgnoreCase)) &&
            Uri.TryCreate(url, UriKind.Absolute, out var remote) && remote.Scheme == Uri.UriSchemeHttps)
            return url;

        var path = Uri.TryCreate(url, UriKind.Absolute, out var absolute) ? absolute.AbsolutePath : url;
        return StoredImage().IsMatch(path) ? path : null;
    }

    [GeneratedRegex(@"^/images/[A-Za-z0-9_-]{8,64}\.(jpg|jpeg|png|webp)$", RegexOptions.IgnoreCase)]
    private static partial Regex StoredImage();
}
