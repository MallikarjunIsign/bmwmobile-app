using AuctionService.Entities;

namespace AuctionService.Services;

/// <summary>
/// Stores listing photos through <see cref="IImageStorage"/> (local disk or Google Cloud Storage): each upload is
/// re-encoded without EXIF data and saved with a ~400 px thumbnail; deleting / copying handles both files.
/// </summary>
public class ListingImageService(IImageStorage storage, ILogger<ListingImageService> logger)
{
    /// <returns>The new image row (not yet added to the item), or null when the bytes aren't a readable image.</returns>
    public async Task<ItemImage?> StoreAsync(byte[] data, string detectedExtension, string itemId,
        CancellationToken cancellationToken = default)
    {
        var processed = ImageProcessor.Process(data, detectedExtension);
        if (processed is null) return null;

        StoredImage? full = null;
        try
        {
            await using (var stream = new MemoryStream(processed.Full))
                full = await storage.SaveAsync(stream, processed.Extension, cancellationToken);
            await using var thumbStream = new MemoryStream(processed.Thumbnail);
            var thumb = await storage.SaveAsync(thumbStream, ".jpg", cancellationToken);

            return new ItemImage
            {
                ItemId = itemId,
                Url = full.Url,
                FileName = full.FileName,
                ThumbnailUrl = thumb.Url,
                ThumbnailFileName = thumb.FileName
            };
        }
        catch
        {
            if (full != null) await storage.DeleteAsync(full.FileName, CancellationToken.None);
            throw;
        }
    }

    /// <summary>Deletes the photo and its thumbnail from storage (seed photos with external URLs are left alone).</summary>
    public async Task DeleteFilesAsync(ItemImage image, CancellationToken cancellationToken = default)
    {
        if (!image.IsStored) return;
        try
        {
            await storage.DeleteAsync(image.FileName, cancellationToken);
            if (image.ThumbnailFileName != null) await storage.DeleteAsync(image.ThumbnailFileName, cancellationToken);
        }
        catch (Exception e) when (e is IOException or ArgumentException)
        {
            logger.LogWarning(e, "Could not delete the files of image {ImageId}", image.Id);
        }
    }

    /// <summary>A copy of the photo for another item (relisting): each listing owns its own files.</summary>
    public async Task<ItemImage> CopyAsync(ItemImage image, string newItemId, CancellationToken cancellationToken = default)
    {
        var copy = new ItemImage
        {
            ItemId = newItemId,
            Url = image.Url,
            FileName = image.FileName,
            ThumbnailUrl = image.ThumbnailUrl,
            ThumbnailFileName = image.ThumbnailFileName,
            Angle = image.Angle,
            SortOrder = image.SortOrder,
            IsPrimary = image.IsPrimary
        };
        if (!image.IsStored) return copy;

        var full = await storage.CopyAsync(image.FileName, cancellationToken);
        copy.Url = full.Url;
        copy.FileName = full.FileName;
        if (image.ThumbnailFileName != null)
        {
            var thumb = await storage.CopyAsync(image.ThumbnailFileName, cancellationToken);
            copy.ThumbnailUrl = thumb.Url;
            copy.ThumbnailFileName = thumb.FileName;
        }
        return copy;
    }
}
