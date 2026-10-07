namespace AuctionService.Services;

/// <param name="FileName">Generated, unique file name (never the uploaded name).</param>
/// <param name="Url">Stored URL: a path under ImageStorage:RequestPath, or an absolute URL for remote storage.</param>
public record StoredImage(string FileName, string Url);

/// <summary>
/// Where uploaded item images live. <see cref="LocalDiskImageStorage"/> is the only implementation today;
/// an Azure Blob / S3 implementation can be registered instead based on ImageStorage:Provider.
/// </summary>
public interface IImageStorage
{
    /// <param name="extension">Extension including the dot (from the detected file type), e.g. ".jpg".</param>
    Task<StoredImage> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default);

    Task DeleteAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>Copies a stored image under a new unique name (used when relisting).</summary>
    Task<StoredImage> CopyAsync(string fileName, CancellationToken cancellationToken = default);
}