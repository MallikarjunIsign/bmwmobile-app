namespace AuctionService.Services;

public class ImageStorageOptions
{
    public const string SectionName = "ImageStorage";

    /// <summary>"Local" (LocalDiskImageStorage). Add e.g. "AzureBlob" / "S3" implementations later.</summary>
    public string Provider { get; set; } = "Local";
    /// <summary>Folder for uploaded files; relative paths are resolved against the content root.</summary>
    public string LocalPath { get; set; } = "uploads";
    /// <summary>Path the files are served under (by this service and the Gateway).</summary>
    public string RequestPath { get; set; } = "/images";
    /// <summary>Public origin clients use to reach the images (the Gateway), e.g. http://localhost:6001.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:6001";
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