using Microsoft.Extensions.Options;

namespace AuctionService.Services;

/// <summary>Stores images in a folder on disk (ImageStorage:LocalPath), served as static files.</summary>
public class LocalDiskImageStorage(IOptions<ImageStorageOptions> options, IWebHostEnvironment environment,
    ILogger<LocalDiskImageStorage> logger) : IImageStorage
{
    private readonly string _root = ResolveRoot(options.Value, environment);

    public static string ResolveRoot(ImageStorageOptions options, IWebHostEnvironment environment) =>
        Path.IsPathRooted(options.LocalPath)
            ? options.LocalPath
            : Path.Combine(environment.ContentRootPath, options.LocalPath);

    public async Task<StoredImage> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_root);

        var fileName = NewFileName(extension);
        var path = Path.Combine(_root, fileName);

        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         bufferSize: 81920, useAsync: true))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        return new StoredImage(fileName, UrlFor(fileName));
    }

    public Task DeleteAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var path = SafePath(fileName);

        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException e)
        {
            // An orphaned file is harmless; don't fail the request over it.
            logger.LogWarning(e, "Could not delete image {FileName}", fileName);
        }

        return Task.CompletedTask;
    }

    public async Task<StoredImage> CopyAsync(string fileName, CancellationToken cancellationToken = default)
    {
        await using var source = File.OpenRead(SafePath(fileName));
        return await SaveAsync(source, Path.GetExtension(fileName), cancellationToken);
    }

    private string UrlFor(string fileName) => $"{options.Value.RequestPath.TrimEnd('/')}/{fileName}";

    private static string NewFileName(string extension) => $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";

    // File names are always ones we generated; refuse anything that could escape the folder.
    private string SafePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            throw new ArgumentException("Invalid image file name", nameof(fileName));

        return Path.Combine(_root, fileName);
    }
}