using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using AuctionService.Errors;
using AuctionService.RequestHelpers;
using AuctionService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine.EntityFrameworkCore;

namespace AuctionService.Controllers;

/// <summary>
/// Listing photos (multipart upload). Every file is checked by its real content (not the extension), size and count,
/// re-encoded without EXIF data (no GPS position leaks) and stored with a ~400 px thumbnail.
/// Limits: parts 3–10 photos, vehicles 8–20 (the minimum is checked when publishing); 5 MB each.
/// After the first bid the seller may still ADD photos, but not delete or reorder them.
/// </summary>
[ApiController]
[Authorize]
[SellerOnly]
[Route("api/auctions/{auctionId}/images")]
public class AuctionImagesController(AuctionDbContext context, IDbContextOutbox<AuctionDbContext> outbox,
    AuctionMapper mapper, ListingImageService images, IOptions<ImageUploadOptions> uploadOptions,
    ILogger<AuctionImagesController> logger) : ControllerBase
{
    private ImageUploadOptions Limits => uploadOptions.Value;

    /// <summary>Uploads one or more images (multipart/form-data, any file field name; optional "angle" field for vehicles).</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ImageUploadLimit]
    public async Task<ActionResult<List<ItemImageDto>>> Upload(string auctionId, CancellationToken cancellationToken)
    {
        var (auction, error) = await LoadForSellerAsync(auctionId, allowWithBids: true);
        if (error != null) return error;

        var item = auction!.Item;
        var upload = await UploadedImages.ReadAsync(Request, Limits, cancellationToken);
        // The count is checked before the content, as before (no file is stored until all of them are valid).
        var form = upload.Form;
        var fileCount = form?.Files.Count ?? 0;
        var max = ListingRules.MaxPhotos(item);
        if (form is not null && fileCount > 0 && item.Images.Count + fileCount > max)
            return BadRequest($"A listing can have at most {max} photos ({item.Images.Count} already uploaded)");
        if (upload.Error != null) return BadRequest(upload.Error);

        var angle = form!.TryGetValue("angle", out var angleValue) ? angleValue.ToString() : null;
        if (!string.IsNullOrEmpty(angle) && !ListingRules.VehicleAngles.Contains(angle))
            return BadRequest($"angle must be one of {string.Join(", ", ListingRules.VehicleAngles)}");

        var accepted = upload.Files;

        var added = new List<ItemImage>();
        try
        {
            var nextOrder = item.Images.Count == 0 ? 0 : item.Images.Max(x => x.SortOrder) + 1;
            var hasPrimary = item.Images.Any(x => x.IsPrimary);

            foreach (var (data, extension) in accepted)
            {
                var image = await images.StoreAsync(data, extension, item.Id, cancellationToken);
                if (image is null) return BadRequest("One of the files is not a readable image");

                image.SortOrder = nextOrder++;
                image.IsPrimary = !hasPrimary;
                image.Angle = string.IsNullOrEmpty(angle) ? null : angle;
                hasPrimary = true;
                item.Images.Add(image);
                added.Add(image);
            }

            await SaveAndPublishAsync(auction);
        }
        catch
        {
            foreach (var image in added) await images.DeleteFilesAsync(image, CancellationToken.None);
            throw;
        }

        return Ok(mapper.ToDto(auction).ImageItems);
    }

    [HttpDelete("{imageId}")]
    public async Task<IActionResult> Delete(string auctionId, string imageId, CancellationToken cancellationToken)
    {
        var (auction, error) = await LoadForSellerAsync(auctionId, allowWithBids: false);
        if (error != null) return error;

        var list = auction!.Item.Images;
        var image = list.FirstOrDefault(x => x.Id == imageId);
        if (image == null) return NotFound();

        list.Remove(image);
        context.ItemImages.Remove(image);
        Normalize(list, primaryId: image.IsPrimary ? null : list.FirstOrDefault(x => x.IsPrimary)?.Id);

        await SaveAndPublishAsync(auction);
        await images.DeleteFilesAsync(image, cancellationToken);

        return NoContent();
    }

    /// <summary>Reorders the images (the first one is the cover). Must list every image id exactly once.</summary>
    [HttpPut("order")]
    public async Task<ActionResult<List<ItemImageDto>>> Reorder(string auctionId, ReorderImagesDto dto)
    {
        var (auction, error) = await LoadForSellerAsync(auctionId, allowWithBids: false);
        if (error != null) return error;

        var list = auction!.Item.Images;
        var ids = dto.ImageIds.Distinct().ToList();

        if (ids.Count != dto.ImageIds.Count || ids.Count != list.Count || ids.Any(id => list.All(x => x.Id != id)))
            return BadRequest("imageIds must list every image of the item exactly once");

        if (dto.PrimaryImageId != null && !ids.Contains(dto.PrimaryImageId))
            return BadRequest("primaryImageId must be one of the item's images");

        foreach (var image in list) image.SortOrder = ids.IndexOf(image.Id);
        Normalize(list, dto.PrimaryImageId ?? ids.FirstOrDefault());

        await SaveAndPublishAsync(auction);

        return Ok(mapper.ToDto(auction).ImageItems);
    }

    private async Task<(Auction? Auction, ActionResult? Error)> LoadForSellerAsync(string auctionId, bool allowWithBids)
    {
        var auction = await context.Auctions.WithDetails().FirstOrDefaultAsync(x => x.Id == auctionId);

        if (auction == null) return (null, NotFound());
        if (auction.Seller != User.Identity?.Name) return (null, Forbid());

        if (auction.Status is not (Status.Live or Status.Draft))
            return (null, BadRequest("Cannot change photos of a listing that has ended"));
        var hasBids = auction.CurrentHighBid > 0 || auction.BidCount > 0;
        if (hasBids && !allowWithBids)
            return (null, BadRequest("After the first bid you can only add photos, not remove or reorder them"));

        return (auction, null);
    }

    // Sort orders become 0..n-1 and exactly one image is primary (the given one, else the first).
    private static void Normalize(List<ItemImage> list, string? primaryId)
    {
        var ordered = list.OrderBy(x => x.SortOrder).ToList();
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
        // Drafts are private: Bid/Search only hear about a listing once it is published.
        if (auction.Status != Status.Draft) await outbox.PublishAsync(mapper.ToUpdated(auction));
        await outbox.SaveChangesAndFlushMessagesAsync();
        logger.LogInformation("Images of auction {AuctionId} changed ({Count} images)", auction.Id, auction.Item.Images.Count);
    }
}
