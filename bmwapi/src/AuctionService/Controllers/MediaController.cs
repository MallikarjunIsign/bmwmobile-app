using AuctionService.Data;
using AuctionService.Entities;
using AuctionService.RequestHelpers;
using AuctionService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AuctionService.Controllers;

public record MediaDto(string Id, string Url, string? ThumbnailUrl);

/// <summary>
/// Photos for chat messages and reviews, uploaded with the same checks and storage as listing photos (real image
/// content, size limit, re-encoded without EXIF, ~400 px thumbnail). At most 5 per upload (a message / review holds 5).
/// </summary>
[ApiController]
[Authorize]
[Route("api/media")]
public class MediaController(AuctionDbContext context, ListingImageService images, AuctionMapper mapper,
    IOptions<ImageUploadOptions> uploadOptions) : ControllerBase
{
    public const int MaxPerUpload = 5;
    private static readonly string[] Purposes = ["chat", "review"];

    /// <summary>multipart/form-data with 1–5 image files; ?purpose=chat (default) or review.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ImageUploadLimit]
    public async Task<ActionResult<List<MediaDto>>> Upload([FromQuery] string? purpose, CancellationToken cancellationToken)
    {
        var use = string.IsNullOrEmpty(purpose) ? "chat" : purpose.ToLowerInvariant();
        if (!Purposes.Contains(use)) return BadRequest(new { message = "purpose must be chat or review" });

        var upload = await UploadedImages.ReadAsync(Request, uploadOptions.Value, cancellationToken);
        if (upload.Form?.Files.Count > MaxPerUpload) return BadRequest(new { message = $"At most {MaxPerUpload} photos at a time" });
        if (upload.Error != null) return BadRequest(new { message = upload.Error });

        var owner = User.Identity?.Name ?? throw new UnauthorizedAccessException();
        var stored = new List<MediaFile>();
        async Task DeleteStoredAsync()
        {
            foreach (var file in stored)
                await images.DeleteFilesAsync(new ItemImage
                {
                    ItemId = "", Url = file.Url, FileName = file.FileName, ThumbnailUrl = file.ThumbnailUrl, ThumbnailFileName = file.ThumbnailFileName
                }, CancellationToken.None);
        }

        try
        {
            foreach (var file in upload.Files)
            {
                var image = await images.StoreAsync(file.Data, file.Extension, use, cancellationToken);
                if (image is null)
                {
                    await DeleteStoredAsync();
                    return BadRequest(new { message = "One of the files is not a readable image" });
                }
                stored.Add(new MediaFile
                {
                    Owner = owner, Purpose = use, Url = image.Url, FileName = image.FileName,
                    ThumbnailUrl = image.ThumbnailUrl, ThumbnailFileName = image.ThumbnailFileName
                });
            }
            context.MediaFiles.AddRange(stored);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await DeleteStoredAsync();
            throw;
        }

        return stored.Select(m => new MediaDto(m.Id, mapper.ImageUrl(m.Url), m.ThumbnailUrl is null ? null : mapper.ImageUrl(m.ThumbnailUrl))).ToList();
    }
}
