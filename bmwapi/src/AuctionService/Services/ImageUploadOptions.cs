namespace AuctionService.Services;

public class ImageUploadOptions
{
    public const string SectionName = "ImageUpload";

    public int MaxFiles { get; set; } = 10;
    public int MaxFileSizeMB { get; set; } = 5;
    /// <summary>Accepted extensions; the file signature must also match one of these formats.</summary>
    public string[] AllowedExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".webp"];

    public long MaxFileSizeBytes => MaxFileSizeMB * 1024L * 1024L;
}