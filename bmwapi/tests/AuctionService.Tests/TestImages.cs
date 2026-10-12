using SkiaSharp;

namespace AuctionService.Tests;

/// <summary>Real (decodable) images for upload tests.</summary>
internal static class TestImages
{
    public static byte[] Png(int width = 64, int height = 48) => Encode(SKEncodedImageFormat.Png, width, height);
    public static byte[] Jpeg(int width = 64, int height = 48) => Encode(SKEncodedImageFormat.Jpeg, width, height);
    public static byte[] Webp(int width = 64, int height = 48) => Encode(SKEncodedImageFormat.Webp, width, height);

    public const string SecretGpsText = "GPS-SECRET-17.3850N-78.4867E";

    /// <summary>A JPEG with an APP1 "Exif" block right after the start marker, containing a fake GPS text.</summary>
    public static byte[] JpegWithExif(int width = 2400, int height = 1800)
    {
        var jpeg = Jpeg(width, height);
        // Minimal TIFF header (big endian, no IFD entries) followed by the "GPS" payload.
        var payload = "Exif\0\0"u8.ToArray()
            .Concat(new byte[] { 0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00 })
            .Concat(System.Text.Encoding.ASCII.GetBytes(SecretGpsText))
            .ToArray();
        var length = payload.Length + 2;
        var segment = new byte[] { 0xFF, 0xE1, (byte)(length >> 8), (byte)(length & 0xFF) }.Concat(payload);
        return jpeg.Take(2).Concat(segment).Concat(jpeg.Skip(2)).ToArray();
    }

    private static byte[] Encode(SKEncodedImageFormat format, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.SteelBlue);
            using var paint = new SKPaint { Color = SKColors.Orange };
            canvas.DrawRect(width / 4f, height / 4f, width / 2f, height / 2f, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }
}
