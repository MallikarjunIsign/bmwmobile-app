using SkiaSharp;

namespace AuctionService.Services;

/// <param name="Full">The photo, at most <c>maxSize</c> px on its longest side.</param>
/// <param name="Thumbnail">About 400 px on its longest side, always JPEG.</param>
public record ProcessedImage(byte[] Full, string Extension, byte[] Thumbnail, int Width, int Height);

/// <summary>
/// Re-encodes every uploaded photo: this removes all EXIF metadata (including the GPS position the phone wrote),
/// applies the EXIF orientation first so the photo stays upright, limits the size, and creates a thumbnail.
/// </summary>
public static class ImageProcessor
{
    public const int MaxSize = 1600;
    public const int ThumbnailSize = 400;

    /// <returns>Null when the bytes can't be decoded as an image.</returns>
    public static ProcessedImage? Process(byte[] data, string extension, int maxSize = MaxSize, int thumbnailSize = ThumbnailSize)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(data));
        if (codec is null) return null;

        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null) return null;

        using var upright = ApplyOrientation(decoded, codec.EncodedOrigin);
        using var full = Fit(upright, maxSize);

        var format = extension switch
        {
            ".png" => SKEncodedImageFormat.Png,
            ".webp" => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Jpeg
        };
        var outExtension = format switch
        {
            SKEncodedImageFormat.Png => ".png",
            SKEncodedImageFormat.Webp => ".webp",
            _ => ".jpg"
        };

        using var thumb = Fit(full, thumbnailSize);
        return new ProcessedImage(Encode(full, format, 85), outExtension, Encode(thumb, SKEncodedImageFormat.Jpeg, 80),
            full.Width, full.Height);
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        // Encoding from pixels writes no EXIF / metadata at all.
        using var encoded = image.Encode(format, quality) ?? image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return encoded.ToArray();
    }

    /// <summary>A copy no larger than <paramref name="max"/> px on the longest side (same size when smaller).</summary>
    private static SKBitmap Fit(SKBitmap source, int max)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= max) return source.Copy();

        var scale = (double)max / longest;
        var info = new SKImageInfo(Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)), SKColorType.Rgba8888, SKAlphaType.Premul);
        return source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)) ?? source.Copy();
    }

    /// <summary>Rotates / flips the pixels the way the EXIF orientation says (the tag itself is dropped).</summary>
    private static SKBitmap ApplyOrientation(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return bitmap.Copy();

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height);
        using var canvas = new SKCanvas(result);

        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Scale(-1, 1, bitmap.Width / 2f, 0);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.RotateDegrees(180, bitmap.Width / 2f, bitmap.Height / 2f);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Scale(1, -1, 0, bitmap.Height / 2f);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.Translate(result.Width, 0);
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1, 0, bitmap.Height / 2f);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(result.Width, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(result.Width, 0);
                canvas.RotateDegrees(90);
                canvas.Scale(-1, -1, bitmap.Width / 2f, bitmap.Height / 2f);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, result.Height);
                canvas.RotateDegrees(-90);
                break;
        }

        canvas.DrawBitmap(bitmap, 0, 0);
        canvas.Flush();
        return result;
    }

    /// <summary>A simple coloured placeholder photo with a caption (demo seed data, no external images).</summary>
    public static byte[] Placeholder(string caption, string subtitle, SKColor background, int width = 1200, int height = 900)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height),
            [background, background.WithAlpha(200)], SKShaderTileMode.Clamp);
        using var fill = new SKPaint { Shader = shader };
        canvas.DrawRect(0, 0, width, height, fill);

        using var font = new SKFont(SKTypeface.Default, 72);
        using var small = new SKFont(SKTypeface.Default, 40);
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var muted = new SKPaint { Color = SKColors.White.WithAlpha(190), IsAntialias = true };
        canvas.DrawText(caption, width / 2f, height / 2f, SKTextAlign.Center, font, white);
        canvas.DrawText(subtitle, width / 2f, height / 2f + 70, SKTextAlign.Center, small, muted);
        canvas.DrawText("ShopCarParts demo photo", width / 2f, height - 50, SKTextAlign.Center, small, muted);
        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 80);
        return encoded.ToArray();
    }
}
