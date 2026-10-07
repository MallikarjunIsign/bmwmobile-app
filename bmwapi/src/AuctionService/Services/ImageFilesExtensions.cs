using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace AuctionService.Services;

public static class ImageFilesExtensions
{
    /// <summary>
    /// Serves uploaded images from the local storage folder under ImageStorage:RequestPath (e.g. /images/x.jpg).
    /// Only image types are served; file names are unique, so responses can be cached forever.
    /// </summary>
    public static WebApplication UseLocalImageFiles(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<ImageStorageOptions>>().Value;
        var root = LocalDiskImageStorage.ResolveRoot(options, app.Environment);
        Directory.CreateDirectory(root);

        var contentTypes = new FileExtensionContentTypeProvider(new Dictionary<string, string>
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp"
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(root),
            RequestPath = options.RequestPath,
            ContentTypeProvider = contentTypes,
            ServeUnknownFileTypes = false,
            OnPrepareResponse = ctx =>
            {
                var headers = ctx.Context.Response.Headers;
                headers[HeaderNames.CacheControl] = "public, max-age=31536000, immutable";
                headers[HeaderNames.XContentTypeOptions] = "nosniff";
            }
        });

        return app;
    }
}