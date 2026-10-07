using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using AuctionService.RequestHelpers;
using AuctionService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine.EntityFrameworkCore;

namespace AuctionService.Controllers;

[ApiController]
[Authorize]
[Route("api/auctions/{auctionId}/images")]
public class AuctionImagesController(AuctionDbContext context, IDbContextOutbox<AuctionDbContext> outbox,
    AuctionMapper mapper, IImageStorage storage, IOptions<ImageUploadOptions> uploadOptions,
    ILogger<AuctionImagesController> logger) : ControllerBase
{
    private ImageUploadOptions Limits => uploadOptions.Value;

    /// <summary>Uploads one or more images (multipart/form-data, any field name).</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ImageUploadLimit]
    public async Task<ActionResult<List<ItemImageDto>>> Upload(string auctionId, CancellationToken cancellationToken)
    {
        var (auction, error) = await LoadForSellerAsync(auctionId);
        if (error != null) return error;

        IFormFileCollection files;
        try
        {
            files = (await Request.ReadFormAsync(cancellationToken)).Files;
        }
        catch (Exception e) when (e is InvalidDataException or BadHttpRequestException)
        {
            return BadRequest($"Upload is too large: max {Limits.MaxFiles} images of {Limits.MaxFileSizeMB} MB each");
        }

        if (files.Count == 0) return BadRequest("No files were uploaded");

        var images = auction!.Item.Images;
        if (images.Count + files.Count > Limits.MaxFiles)
            return BadRequest($"An item can have at most {Limits.MaxFiles} images ({images.Count} already uploaded)");

        // Validate every file before storing any of them.
        var accepted = new List<(IFormFile File, string Extension)>();
        foreach (var file in files)
        {
            var name = Path.GetFileName(file.FileName);

            if (file.Length == 0) return BadRequest($"{name} is empty");
            if (file.Length > Limits.MaxFileSizeBytes)
                return BadRequest($"{name} is larger than {Limits.MaxFileSizeMB} MB");

            var extension = Path.GetExtension(name).ToLowerInvariant();
            if (!Limits.AllowedExtensions.Contains(extension))
                return BadRequest($"{name} is not an allowed type ({string.Join(", ", Limits.AllowedExtensions)})");

            await using var stream = file.OpenReadStream();
            // The content decides the type (and the stored extension), not the uploaded name.
            var detected = await ImageSignature.DetectAsync(stream, cancellationToken);
            if (detected == null || !IsAllowed(detected))
                return BadRequest($"{name} is not a valid {string.Join(", ", Limits.AllowedExtensions)} image");

            accepted.Add((file, detected));
        }

        var stored = new List<StoredImage>();
        try
        {
            var nextOrder = images.Count == 0 ? 0 : images.Max(x => x.SortOrder) + 1;
            var hasPrimary = images.Any(x => x.IsPrimary);

            foreach (var (file, extension) in accepted)
            {
                await using var stream = file.OpenReadStream();
                var saved = await storage.SaveAsync(stream, extension, cancellationToken);
                stored.Add(saved);

                images.Add(new ItemImage
                {
                    ItemId = auction.Item.Id,
                    Url = saved.Url,
                    FileName = saved.FileName,
                    SortOrder = nextOrder++,
                    IsPrimary = !hasPrimary
                });
                hasPrimary = true;
            }

            await SaveAndPublishAsync(auction);
        }
        catch
        {
            foreach (var saved in stored) await storage.DeleteAsync(saved.FileName, CancellationToken.None);
            throw;
        }

        return Ok(mapper.ToDto(auction).ImageItems);
    }

    [HttpDelete("{imageId}")]
    public async Task<IActionResult> Delete(string auctionId, string imageId, CancellationToken cancellationToken)
    {
        var (auction, error) = await LoadForSellerAsync(auctionId);
        if (error != null) return error;

        var images = auction!.Item.Images;
        var image = images.FirstOrDefault(x => x.Id == imageId);
        if (image == null) return NotFound();

        images.Remove(image);
        context.ItemImages.Remove(image);
        Normalize(images, primaryId: image.IsPrimary ? null : images.FirstOrDefault(x => x.IsPrimary)?.Id);

        await SaveAndPublishAsync(auction);

        if (!image.IsExternal) await storage.DeleteAsync(image.FileName, cancellationToken);

        return NoContent();
    }

    /// <summary>Reorders the images and sets the primary one. Must list every image id exactly once.</summary>
    [HttpPut("order")]
    public async Task<ActionResult<List<ItemImageDto>>> Reorder(string auctionId, ReorderImagesDto dto)
    {
        var (auction, error) = await LoadForSellerAsync(auctionId);
        if (error != null) return error;

        var images = auction!.Item.Images;
        var ids = dto.ImageIds.Distinct().ToList();

        if (ids.Count != dto.ImageIds.Count || ids.Count != images.Count || ids.Any(id => images.All(x => x.Id != id)))
            return BadRequest("imageIds must list every image of the item exactly once");

        if (dto.PrimaryImageId != null && !ids.Contains(dto.PrimaryImageId))
            return BadRequest("primaryImageId must be one of the item's images");

        foreach (var image in images) image.SortOrder = ids.IndexOf(image.Id);
        Normalize(images, dto.PrimaryImageId ?? ids.FirstOrDefault());

        await SaveAndPublishAsync(auction);

        return Ok(mapper.ToDto(auction).ImageItems);
    }

    private bool IsAllowed(string detectedExtension) =>
        Limits.AllowedExtensions.Contains(detectedExtension) ||
        detectedExtension == ".jpg" && Limits.AllowedExtensions.Contains(".jpeg");

    private async Task<(Auction? Auction, ActionResult? Error)> LoadForSellerAsync(string auctionId)
    {
        var auction = await context.Auctions.WithDetails().FirstOrDefaultAsync(x => x.Id == auctionId);

        if (auction == null) return (null, NotFound());
        if (auction.Seller != User.Identity?.Name) return (null, Forbid());

        // Images follow the same "no edits after a bid" rule as the rest of the listing.
        if (auction.CurrentHighBid > 0) return (null, BadRequest("Cannot change images of an auction that has bids"));
        if (auction.Status != Status.Live) return (null, BadRequest("Cannot change images of an auction that has ended"));

        return (auction, null);
    }

    // Sort orders become 0..n-1 and exactly one image is primary (the given one, else the first).
    private static void Normalize(List<ItemImage> images, string? primaryId)
    {
        var ordered = images.OrderBy(x => x.SortOrder).ToList();
        primaryId ??= ordered.FirstOrDefault()?.Id;

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SortOrder = i;
            ordered[i].IsPrimary = ordered[i].Id == primaryId;
        }
    }

    private async Task SaveAndPublishAsync(Auction auction)
    {
        auction.UpdatedAt = DateTime.UtcNow;
        await outbox.PublishAsync(mapper.ToUpdated(auction));
        await outbox.SaveChangesAndFlushMessagesAsync();
        logger.LogInformation("Images of auction {AuctionId} changed ({Count} images)", auction.Id, auction.Item.Images.Count);
    }
}