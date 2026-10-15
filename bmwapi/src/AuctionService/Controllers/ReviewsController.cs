using Contracts;
using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using AuctionService.RequestHelpers;
using AuctionService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace AuctionService.Controllers;

/// <summary>
/// Ratings and reviews. Only the buyer of a COMPLETED order can review it (verified purchase), once, up to 30 days
/// after completion, and edit it within 7 days. The seller can post one public reply. Anyone signed in can report a
/// review; admins can hide it. Every change recalculates the seller's rating (SellerRatingChanged).
/// </summary>
[ApiController]
[Authorize]
[Route("api/reviews")]
public class ReviewsController(AuctionDbContext context, IDbContextOutbox<AuctionDbContext> outbox, ReviewService reviews,
    AuctionMapper mapper, IConfiguration configuration, TimeProvider clock) : ControllerBase
{
    private string? Me => User.Identity?.Name;
    private string Username => Me ?? throw new UnauthorizedAccessException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // Admins: token role "admin", or listed by username in the "Admin:Usernames" setting.
    private bool IsAdmin => AppRoles.IsAdmin(User) || Me is { } user &&
        (configuration.GetSection("Admin:Usernames").Get<string[]>() ?? []).Contains(user, StringComparer.OrdinalIgnoreCase);

    [HttpPost]
    public async Task<ActionResult<ReviewDto>> Create(SaveReviewDto dto)
    {
        var order = await context.Orders.FirstOrDefaultAsync(o => o.Id == dto.OrderId);
        if (order is null || (order.Buyer != Username && order.Seller != Username)) return NotFound();
        var exists = await context.Reviews.AnyAsync(r => r.OrderId == order.Id);
        if (ReviewService.CannotReview(order, Username, exists, Now) is { } problem)
            return problem.StartsWith("Only") ? Forbid() : BadRequest(new { message = problem });

        var (photos, error) = await PhotosAsync(dto);
        if (error != null) return error;

        var review = new Review
        {
            OrderId = order.Id, ListingId = order.AuctionId, Buyer = order.Buyer, Seller = order.Seller,
            ItemTitle = order.Title, ItemImageUrl = order.ImageUrl, CreatedAt = Now
        };
        Apply(review, dto, photos!);
        context.Reviews.Add(review);
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Posted twice at the same moment.
            return BadRequest(new { message = "You've already reviewed this order" });
        }

        await PublishRatingAsync(review.Seller);
        return CreatedAtAction(nameof(Get), new { id = review.Id }, ToDto(review, Username, Now, mapper));
    }

    [AllowAnonymous]
    [HttpGet("{id}")]
    public async Task<ActionResult<ReviewDto>> Get(string id)
    {
        var review = await context.Reviews.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (review is null || (review.Status == ReviewStatus.Hidden && review.Buyer != Me && !IsAdmin)) return NotFound();
        return ToDto(review, Me, Now, mapper);
    }

    /// <summary>The author edits the review within 7 days of posting.</summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<ReviewDto>> Update(string id, SaveReviewDto dto)
    {
        var review = await context.Reviews.FirstOrDefaultAsync(r => r.Id == id);
        if (review is null) return NotFound();
        if (review.Buyer != Username) return Forbid();
        if (!ReviewService.CanEdit(review, Username, Now)) return BadRequest(new { message = "Reviews can be edited within 7 days of posting" });

        var (photos, error) = await PhotosAsync(dto, review);
        if (error != null) return error;
        Apply(review, dto, photos!);
        review.UpdatedAt = Now;
        await context.SaveChangesAsync();

        await PublishRatingAsync(review.Seller);
        return ToDto(review, Username, Now, mapper);
    }

    /// <summary>The seller's one public reply.</summary>
    [HttpPost("{id}/reply")]
    public async Task<ActionResult<ReviewDto>> Reply(string id, ReviewReplyDto dto)
    {
        var review = await context.Reviews.FirstOrDefaultAsync(r => r.Id == id);
        if (review is null || review.Status == ReviewStatus.Hidden) return NotFound();
        if (review.Seller != Username) return Forbid();
        if (review.SellerReply != null) return BadRequest(new { message = "You've already replied to this review" });

        var text = dto.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return BadRequest(new { message = "Write your reply" });
        if (text.Length > ReviewService.MaxCommentLength) return BadRequest(new { message = "Keep the reply under 1000 characters" });
        if (ListingRules.ContainsContactDetails(text)) return BadRequest(new { message = "Please don't put phone numbers or e-mail addresses in replies" });

        review.SellerReply = text;
        review.SellerReplyAt = Now;
        await context.SaveChangesAsync();
        return ToDto(review, Username, Now, mapper);
    }

    [HttpPost("{id}/report")]
    public async Task<IActionResult> Report(string id, ReviewReportDto dto)
    {
        if (!await context.Reviews.AnyAsync(r => r.Id == id && r.Status == ReviewStatus.Published)) return NotFound();
        var reason = dto.Reason?.Trim().ToLowerInvariant();
        if (reason is null || !ReviewService.ReportReasons.Contains(reason))
            return BadRequest(new { message = "Choose a reason: spam, abuse, fake, personal or other" });
        var details = dto.Details?.Trim();
        if (details?.Length > 500) return BadRequest(new { message = "Keep the details under 500 characters" });

        context.ReviewReports.Add(new ReviewReport
        {
            ReviewId = id, Reporter = Username, Reason = reason, Details = string.IsNullOrEmpty(details) ? null : details, CreatedAt = Now
        });
        await context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Item reviews of a listing (listing detail of sold items), newest first.</summary>
    [AllowAnonymous]
    [HttpGet("listing/{listingId}")]
    public async Task<ActionResult<List<ReviewDto>>> ForListing(string listingId)
    {
        var list = await context.Reviews.AsNoTracking().Where(r => r.ListingId == listingId && r.Status == ReviewStatus.Published)
            .OrderByDescending(r => r.CreatedAt).Take(50).ToListAsync();
        return list.Select(r => ToDto(r, Me, Now, mapper)).ToList();
    }

    // ── admin ──────────────────────────────────────────────────────────

    /// <summary>Open review reports, with the review.</summary>
    [HttpGet("admin/reports")]
    public async Task<IActionResult> Reports()
    {
        if (!IsAdmin) return Forbid();
        var reports = await context.ReviewReports.AsNoTracking().Where(r => r.Status == "Open").OrderByDescending(r => r.CreatedAt).Take(100).ToListAsync();
        var ids = reports.Select(r => r.ReviewId).Distinct().ToList();
        var reviewed = await context.Reviews.AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
        return Ok(reports.Select(r => new
        {
            r.Id, r.ReviewId, r.Reporter, r.Reason, r.Details, r.CreatedAt,
            Review = reviewed.TryGetValue(r.ReviewId, out var review) ? ToDto(review, Me, Now, mapper) : null
        }));
    }

    /// <summary>Hide a review (not shown, not counted); its open reports are closed.</summary>
    [HttpPost("{id}/hide")]
    public Task<IActionResult> Hide(string id) => SetStatusAsync(id, ReviewStatus.Hidden);

    [HttpPost("{id}/show")]
    public Task<IActionResult> Show(string id) => SetStatusAsync(id, ReviewStatus.Published);

    private async Task<IActionResult> SetStatusAsync(string id, ReviewStatus status)
    {
        if (!IsAdmin) return Forbid();
        var review = await context.Reviews.FirstOrDefaultAsync(r => r.Id == id);
        if (review is null) return NotFound();
        review.Status = status;
        if (status == ReviewStatus.Hidden)
            foreach (var report in await context.ReviewReports.Where(r => r.ReviewId == id && r.Status == "Open").ToListAsync())
                report.Status = "Closed";
        await context.SaveChangesAsync();
        await PublishRatingAsync(review.Seller);
        return NoContent();
    }

    // ── helpers ────────────────────────────────────────────────────────

    private async Task PublishRatingAsync(string seller)
    {
        var changed = await reviews.RecalculateAsync(seller, Now);
        await outbox.PublishAsync(changed);
        await outbox.SaveChangesAndFlushMessagesAsync();
    }

    /// <summary>The photos: the caller's own review uploads (photos already on the review stay allowed when editing).</summary>
    private async Task<(List<ReviewPhoto>? Photos, ActionResult? Error)> PhotosAsync(SaveReviewDto dto, Review? existing = null)
    {
        var ids = dto.PhotoIds.Distinct().ToList();
        var errors = ReviewService.Validate(dto.ItemRating, dto.SellerRating, dto.Communication, dto.Accuracy, dto.Delivery,
            dto.Tags.Distinct().ToList(), dto.Comment?.Trim(), ids.Count);
        if (errors.Count > 0) return (null, Invalid(errors));

        var kept = existing?.Photos.Where(p => ids.Contains(p.MediaId)).ToList() ?? [];
        var newIds = ids.Except(kept.Select(p => p.MediaId)).ToList();
        var media = await context.MediaFiles.AsNoTracking()
            .Where(m => newIds.Contains(m.Id) && m.Owner == Username && m.Purpose == "review").ToListAsync();
        if (media.Count != newIds.Count) return (null, Invalid(new() { ["photos"] = ["Photos must be uploaded by you for this review"] }));

        var all = kept.Concat(media.Select(m => new ReviewPhoto { MediaId = m.Id, Url = m.Url, ThumbnailUrl = m.ThumbnailUrl })).ToList();
        // Keep the order the app sent.
        return (all.OrderBy(p => ids.IndexOf(p.MediaId)).ToList(), null);
    }

    private static void Apply(Review review, SaveReviewDto dto, List<ReviewPhoto> photos)
    {
        review.ItemRating = dto.ItemRating;
        review.SellerRating = dto.SellerRating;
        review.Communication = dto.Communication;
        review.Accuracy = dto.Accuracy;
        review.Delivery = dto.Delivery;
        review.Tags = dto.Tags.Distinct().ToList();
        review.Comment = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment.Trim();
        review.Photos = photos;
    }

    private ActionResult Invalid(Dictionary<string, string[]> errors)
    {
        foreach (var (field, messages) in errors)
            foreach (var message in messages)
                ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }

    public static ReviewDto ToDto(Review r, string? user, DateTime now, AuctionMapper mapper) => new()
    {
        Id = r.Id,
        OrderId = r.OrderId,
        ListingId = r.ListingId,
        ItemTitle = r.ItemTitle,
        ItemImageUrl = r.ItemImageUrl,
        Buyer = user == r.Buyer ? r.Buyer : ReviewService.Mask(r.Buyer),
        Seller = r.Seller,
        ItemRating = r.ItemRating,
        SellerRating = r.SellerRating,
        Communication = r.Communication,
        Accuracy = r.Accuracy,
        Delivery = r.Delivery,
        Tags = r.Tags,
        Comment = r.Comment,
        Photos = r.Photos.Select(p => new ReviewPhotoDto(p.MediaId, mapper.ImageUrl(p.Url), p.ThumbnailUrl is null ? null : mapper.ImageUrl(p.ThumbnailUrl))).ToList(),
        SellerReply = r.SellerReply,
        SellerReplyAt = r.SellerReplyAt,
        Status = r.Status.ToString(),
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        CanEdit = ReviewService.CanEdit(r, user, now),
        CanReply = user == r.Seller && r.SellerReply is null && r.Status == ReviewStatus.Published,
        IsMine = user == r.Buyer
    };
}

/// <summary>A seller's public ratings: average, breakdown, quick-rating averages, completed sales, reviews.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/sellers")]
public class SellersController(AuctionDbContext context, AuctionMapper mapper, TimeProvider clock) : ControllerBase
{
    [HttpGet("{username}")]
    public async Task<ActionResult<SellerRatingDto>> Get(string username)
    {
        var summary = await context.SellerRatings.AsNoTracking().FirstOrDefaultAsync(s => s.Seller == username);
        var sales = await context.Orders.CountAsync(o => o.Seller == username && o.Status == OrderStatus.Completed);
        var latest = await context.Reviews.AsNoTracking().Where(r => r.Seller == username && r.Status == ReviewStatus.Published)
            .OrderByDescending(r => r.CreatedAt).Take(5).ToListAsync();
        var me = User.Identity?.Name;
        var now = clock.GetUtcNow().UtcDateTime;
        var count = summary?.Count ?? 0;

        return new SellerRatingDto
        {
            Seller = username,
            Average = count == 0 ? null : summary!.Average,
            Count = count,
            IsNewSeller = count < ReviewService.NewSellerBelow,
            Breakdown = summary is null ? [0, 0, 0, 0, 0] : [summary.Stars5, summary.Stars4, summary.Stars3, summary.Stars2, summary.Stars1],
            CommunicationAverage = summary?.CommunicationAverage,
            AccuracyAverage = summary?.AccuracyAverage,
            DeliveryAverage = summary?.DeliveryAverage,
            ItemAverage = summary?.ItemAverage,
            CompletedSales = sales,
            LatestReviews = latest.Select(r => ReviewsController.ToDto(r, me, now, mapper)).ToList()
        };
    }

    /// <summary>The seller's reviews, newest first, 20 per page.</summary>
    [HttpGet("{username}/reviews")]
    public async Task<ActionResult<List<ReviewDto>>> Reviews(string username, [FromQuery] int page = 1)
    {
        var list = await context.Reviews.AsNoTracking().Where(r => r.Seller == username && r.Status == ReviewStatus.Published)
            .OrderByDescending(r => r.CreatedAt).Skip((Math.Max(page, 1) - 1) * 20).Take(20).ToListAsync();
        var now = clock.GetUtcNow().UtcDateTime;
        return list.Select(r => ReviewsController.ToDto(r, User.Identity?.Name, now, mapper)).ToList();
    }
}
