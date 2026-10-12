using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;

namespace AuctionService.Services;

/// <summary>
/// Stores photos in a Google Cloud Storage bucket (ImageStorage:Provider = "GoogleCloudStorage").
/// The bucket name comes from settings (ImageStorage:Gcs:Bucket); credentials ONLY from the environment
/// (GOOGLE_APPLICATION_CREDENTIALS pointing at a service-account key file, or the workload identity when running
/// in Google Cloud) – never from the repository. Objects get public URLs under ImageStorage:Gcs:PublicBaseUrl
/// (default https://storage.googleapis.com/{bucket}); make the bucket (or the prefix) publicly readable.
/// </summary>
public class GoogleCloudImageStorage : IImageStorage
{
    private readonly StorageClient _client;
    private readonly GcsOptions _options;
    private readonly ILogger<GoogleCloudImageStorage> _logger;

    public GoogleCloudImageStorage(IOptions<ImageStorageOptions> options, ILogger<GoogleCloudImageStorage> logger)
        : this(StorageClient.Create(), options, logger)
    {
    }

    public GoogleCloudImageStorage(StorageClient client, IOptions<ImageStorageOptions> options,
        ILogger<GoogleCloudImageStorage> logger)
    {
        _options = options.Value.Gcs;
        if (string.IsNullOrWhiteSpace(_options.Bucket))
            throw new InvalidOperationException("ImageStorage:Gcs:Bucket is required for the GoogleCloudStorage provider");
        _client = client;
        _logger = logger;
    }

    public async Task<StoredImage> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default)
    {
        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        await _client.UploadObjectAsync(_options.Bucket, ObjectName(fileName), ImageSignature.ContentType(extension),
            content, cancellationToken: cancellationToken);
        return new StoredImage(fileName, UrlFor(fileName));
    }

    public async Task DeleteAsync(string fileName, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.DeleteObjectAsync(_options.Bucket, ObjectName(fileName), cancellationToken: cancellationToken);
        }
        catch (Google.GoogleApiException e) when (e.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
        }
        catch (Exception e)
        {
            // An orphaned object is harmless; don't fail the request over it.
            _logger.LogWarning(e, "Could not delete image {FileName} from GCS", fileName);
        }
    }

    public async Task<StoredImage> CopyAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var copy = $"{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        await _client.CopyObjectAsync(_options.Bucket, ObjectName(fileName), _options.Bucket, ObjectName(copy),
            cancellationToken: cancellationToken);
        return new StoredImage(copy, UrlFor(copy));
    }

    private string ObjectName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            throw new ArgumentException("Invalid image file name", nameof(fileName));
        return $"{_options.ObjectPrefix.Trim('/')}/{fileName}".TrimStart('/');
    }

    private string UrlFor(string fileName)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.PublicBaseUrl)
            ? $"https://storage.googleapis.com/{_options.Bucket}"
            : _options.PublicBaseUrl.TrimEnd('/');
        return $"{baseUrl}/{ObjectName(fileName)}";
    }
}
