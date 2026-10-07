namespace AuctionService.Services;

/// <summary>Detects the real image type from the first bytes of the file (magic numbers).</summary>
public static class ImageSignature
{
    public const int HeaderLength = 12;

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] Webp = "WEBP"u8.ToArray();

    /// <returns>".jpg", ".png" or ".webp", or null when the content is none of those.</returns>
    public static string? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(Jpeg)) return ".jpg";
        if (header.StartsWith(Png)) return ".png";
        if (header.Length >= 12 && header[..4].SequenceEqual(Riff) && header[8..12].SequenceEqual(Webp)) return ".webp";
        return null;
    }

    public static async Task<string?> DetectAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[HeaderLength];
        var read = await stream.ReadAtLeastAsync(buffer, HeaderLength, throwOnEndOfStream: false, cancellationToken);
        return Detect(buffer.AsSpan(0, read));
    }

    public static string ContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };
}