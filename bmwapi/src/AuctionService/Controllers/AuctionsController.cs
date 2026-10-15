
using System.Globalization;
using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using AuctionService.Errors;
using AuctionService.RequestHelpers;
using AuctionService.Services;
using AuctionService.Validation;
using Contracts;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace AuctionService.Controllers;

[ApiController]
[Route("api/[controller]")] // http://localhost:7001/api/auctions GET, POST, PUT
public class AuctionsController(AuctionDbContext context, IDbContextOutbox<AuctionDbContext> outbox,
    AuctionMapper mapper, CustomOrderMatcher customOrders) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AuctionDto>>> GetAuctions(string? date)
    {
        // Drafts and cancelled listings are private to the seller (and never indexed by SearchService).
        var query = context.Auctions.AsNoTracking().WithDetails().Published();

        if (!string.IsNullOrEmpty(date))
        {
            if (!DateTime.TryParse(date, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal
                    | DateTimeStyles.AssumeUniversal, out var parsedDate))
            {
                return BadRequest("Invalid date");
            }

            query = query.Where(x => x.UpdatedAt > parsedDate);
        }

        var auctions = await query
            .OrderBy(x => x.Item.Make)
            .ThenBy(x => x.Item.Model)
            .ToListAsync();

        // The list is public (and feeds the search index), so the reserve is never included here.
        return auctions.Select(x => mapper.ToDto(x)).ToList();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AuctionDto>> GetAuction(string id)
    {
        var auction = await context.Auctions
            .AsNoTracking()
            .WithDetails()
            .FirstOrDefaultAsync(x => x.Id == id);

        if (auction == null || auction.Status is Status.Draft or Status.Cancelled && auction.Seller != User.Identity?.Name)
        {
            return NotFound();
        }

        // The seller (authenticated) also gets their own reserve price and full VIN.
        return mapper.ToDto(auction, User.Identity?.Name);
    }

    [Authorize]
    [SellerOnly]
    [HttpPost]
    public async Task<ActionResult<AuctionDto>> CreateAuction(CreateAuctionDto createAuctionDto)
    {
        var seller = User.Identity?.Name ?? throw new Exception("User has not been authenticated");
        var auction = AuctionMapper.ToEntity(createAuctionDto, seller);
        SellerLocation.ApplyDefault(auction.Item, User);

        var invalid = await ValidateAsync(auction, requireFutureEnd: true);
        if (invalid != null) return invalid;

        context.Auctions.Add(auction);
        await LoadContainerAsync(auction);

        await outbox.PublishAsync(mapper.ToCreated(auction));
        // Buyers waiting for this part (custom orders) are notified in the same transaction.
        await customOrders.MatchNewListingAsync(auction, outbox);
        await outbox.SaveChangesAndFlushMessagesAsync();

        return CreatedAtAction(nameof(GetAuction), new { id = auction.Id }, mapper.ToDto(auction, seller));
    }

    [Authorize]
    [SellerOnly]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAuction(string id, UpdateAuctionDto updateAuctionDto)
    {
        var auction = await context.Auctions
            .WithDetails()
            .FirstOrDefaultAsync(x => x.Id == id);

        if (updateAuctionDto.Make == "foo") throw new Exception("bar");

        if (auction == null)
        {
            return NotFound();
        }

        if (auction.Seller != User.Identity?.Name) return Forbid();

        // No edits once bidding has started: this covers the prices, ItemType and AuctionEnd too.
        if (auction.CurrentHighBid > 0)
        {
            return BadRequest("Cannot update an auction that has bids");
        }

        if (auction.Status != Status.Live)
        {
            return BadRequest("Cannot update an auction that has ended");
        }

        var endChanged = updateAuctionDto.AuctionEnd is not null && updateAuctionDto.AuctionEnd != auction.AuctionEnd;
        AuctionMapper.ApplyUpdate(updateAuctionDto, auction);

        var invalid = await ValidateAsync(auction, requireFutureEnd: endChanged);
        if (invalid != null) return invalid;

        auction.UpdatedAt = DateTime.UtcNow;
        await LoadContainerAsync(auction);

        await outbox.PublishAsync(mapper.ToUpdated(auction));
        await outbox.SaveChangesAndFlushMessagesAsync();

        return NoContent();
    }

    [Authorize]
    [SellerOnly]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAuction(string id, [FromServices] ListingImageService images)
    {
        var auction = await context.Auctions
            .Include(x => x.Item).ThenInclude(x => x.Images)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (auction == null)
        {
            return NotFound();
        }

        if (auction.CurrentHighBid > 0)
        {
            return BadRequest("Cannot delete auction that has bids");
        }

        if (auction.Seller != User.Identity?.Name) return Forbid();

        context.Auctions.Remove(auction);

        await outbox.PublishAsync(auction.Adapt<AuctionDeleted>());
        await outbox.SaveChangesAndFlushMessagesAsync();

        // Image rows are cascade-deleted with the item; remove the uploaded files once that is committed.
        foreach (var image in auction.Item.Images)
            await images.DeleteFilesAsync(image);

        return NoContent();
    }

    /// <summary>
    /// Seller relists an Unsold / ReserveNotMet auction: a new auction with the same item, images (files are
    /// copied) and container, and new prices and end time.
    /// </summary>
    [Authorize]
    [SellerOnly]
    [HttpPost("{id}/relist")]
    public async Task<ActionResult<AuctionDto>> RelistAuction(string id, RelistAuctionDto dto,
        [FromServices] ListingImageService images)
    {
        var source = await context.Auctions.AsNoTracking().WithDetails().FirstOrDefaultAsync(x => x.Id == id);
        if (source == null) return NotFound();
        if (source.Seller != User.Identity?.Name) return Forbid();
        if (source.Status is not (Status.Unsold or Status.ReserveNotMet))
            return BadRequest("Only unsold auctions (or auctions whose reserve was not met) can be relisted");

        var auction = AuctionMapper.CopyForRelist(source, dto);

        var invalid = await ValidateAsync(auction, requireFutureEnd: true);
        if (invalid != null) return invalid;

        var copied = new List<ItemImage>();
        try
        {
            foreach (var image in source.Item.OrderedImages)
            {
                // Uploaded files are copied so each listing owns its files; seed images are external URLs.
                var copy = await images.CopyAsync(image, auction.Id);
                copy.SortOrder = auction.Item.Images.Count;
                copy.IsPrimary = auction.Item.Images.Count == 0;
                if (image.IsStored) copied.Add(copy);
                auction.Item.Images.Add(copy);
            }

            context.Auctions.Add(auction);
            await LoadContainerAsync(auction);

            await outbox.PublishAsync(mapper.ToCreated(auction));
            await customOrders.MatchNewListingAsync(auction, outbox);
            await outbox.SaveChangesAndFlushMessagesAsync();
        }
        catch
        {
            foreach (var image in copied) await images.DeleteFilesAsync(image);
            throw;
        }

        return CreatedAtAction(nameof(GetAuction), new { id = auction.Id }, mapper.ToDto(auction, auction.Seller));
    }

    [Authorize]
    [HttpPost("test")]
    public ActionResult<string> AuthTest()
    {
        var name = User.Identity?.Name;

        return Ok($"{name} has been authenticated");
    }

    private async Task<ActionResult?> ValidateAsync(Auction auction, bool requireFutureEnd)
    {
        var errors = AuctionValidator.Validate(auction, requireFutureEnd);

        var containerId = auction.Item.ContainerId;
        if (containerId != null && !await context.Containers.AnyAsync(x => x.Id == containerId))
            errors["containerId"] = ["Container not found"];

        if (errors.Count == 0) return null;

        foreach (var (field, messages) in errors)
            foreach (var message in messages)
                ModelState.AddModelError(field, message);

        return ValidationProblem(ModelState);
    }

    private async Task LoadContainerAsync(Auction auction)
    {
        var item = auction.Item;
        item.Container = item.ContainerId == null
            ? null
            : await context.Containers.FindAsync(item.ContainerId);
    }
}