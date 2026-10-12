namespace AuctionService.Services;

public class ImageStorageOptions
{
    public const string SectionName = "ImageStorage";

    /// <summary>"Local" (LocalDiskImageStorage, default for development) or "GoogleCloudStorage".</summary>
    public string Provider { get; set; } = "Local";
    public GcsOptions Gcs { get; set; } = new();
    /// <summary>Folder for uploaded files; relative paths are resolved against the content root.</summary>
    public string LocalPath { get; set; } = "uploads";
    /// <summary>Path the files are served under (by this service and the Gateway).</summary>
    public string RequestPath { get; set; } = "/images";
    /// <summary>Public origin clients use to reach the images (the Gateway), e.g. http://localhost:6001.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:6001";
}

/// <summary>
/// "ImageStorage:Gcs" – Google Cloud Storage. Credentials are NOT configured here: the Google client reads them from
/// the environment (GOOGLE_APPLICATION_CREDENTIALS = path of a service-account key file, kept outside the repo).
/// </summary>
public class GcsOptions
{
    public string Bucket { get; set; } = "";
    /// <summary>Folder inside the bucket, e.g. "listings".</summary>
    public string ObjectPrefix { get; set; } = "listings";
    /// <summary>Optional CDN / custom domain; default https://storage.googleapis.com/{bucket}.</summary>
    public string? PublicBaseUrl { get; set; }
}

/// <summary>Turns a stored image URL ("/images/x.jpg" or an absolute seed URL) into a public URL.</summary>
public class ImageUrlBuilder(Microsoft.Extensions.Options.IOptions<ImageStorageOptions> options)
{
    public string Build(string storedUrl)
    {
        if (storedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            storedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return storedUrl;

        return options.Value.PublicBaseUrl.TrimEnd('/') + "/" + storedUrl.TrimStart('/');
    }
}