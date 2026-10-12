using AuctionService.Data;
using AuctionService.Entities;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Services;

/// <summary>Review rules (who, when, what) and the seller rating summary.</summary>
public class ReviewService(AuctionDbContext db)
{
    /// <summary>A review can be written up to 30 days after the order was completed …</summary>
    public static readonly TimeSpan WriteWindow = TimeSpan.FromDays(30);
    /// <summary>… edited within 7 days of posting …</summary>
    public static readonly TimeSpan EditWindow = TimeSpan.FromDays(7);
    /// <summary>… and the buyer is reminded 2 days after completion if they haven't.</summary>
    public static readonly TimeSpan ReminderAfter = TimeSpan.FromDays(2);
    /// <summary>"New seller" until this many ratings.</summary>
    public const int NewSellerBelow = 3;
    public const int MaxCommentLength = 1000;
    public const int MaxPhotos = 5;

    public static readonly string[] Tags =
        ["Fast replies", "Well packed", "As described", "On time", "Not as described", "Slow to respond"];

    public static readonly string[] ReportReasons = ["spam", "abuse", "fake", "personal", "other"];

    /// <summary>Why the user can't review this order now (null = they can).</summary>
    public static string? CannotReview(Order order, string user, bool alreadyReviewed, DateTime now)
    {
        if (order.Buyer != user) return "Only the buyer can review this order";
        if (order.Seller == user) return "You can't review your own sale";
        if (order.Status != OrderStatus.Completed || order.CompletedAt is null) return "You can review it once the order is completed";
        if (alreadyReviewed) return "You've already reviewed this order";
        if (now > order.CompletedAt.Value + WriteWindow) return "Reviews can be written up to 30 days after the order is completed";
        return null;
    }

    public static bool CanEdit(Review review, string? user, DateTime now) =>
        user == review.Buyer && now <= review.CreatedAt + EditWindow;

    /// <summary>Checks the ratings, tags and comment (field → messages, like the listing rules).</summary>
    public static Dictionary<string, string[]> Validate(int itemRating, int sellerRating, int? communication, int? accuracy, int? delivery,
        IReadOnlyCollection<string> tags, string? comment, int photoCount)
    {
        var errors = new Dictionary<string, string[]>();
        if (itemRating is < 1 or > 5) errors["itemRating"] = ["Rate the item from 1 to 5 stars"];
        if (sellerRating is < 1 or > 5) errors["sellerRating"] = ["Rate the seller from 1 to 5 stars"];
        foreach (var (name, value) in new[] { ("communication", communication), ("accuracy", accuracy), ("delivery", delivery) })
            if (value is < 1 or > 5) errors[name] = ["Quick ratings are 1 to 5 stars"];
        var unknown = tags.Where(t => !Tags.Contains(t)).ToList();
        if (unknown.Count > 0) errors["tags"] = [$"Unknown tag: {string.Join(", ", unknown)}"];
        if (comment?.Length > MaxCommentLength) errors["comment"] = [$"Keep the comment under {MaxCommentLength} characters"];
        else if (ListingRules.ContainsContactDetails(comment)) errors["comment"] = ["Please don't put phone numbers or e-mail addresses in reviews"];
        if (photoCount > MaxPhotos) errors["photos"] = [$"At most {MaxPhotos} photos"];
        return errors;
    }

    /// <summary>"alice" → "a***e" (reviewers are shown partly hidden).</summary>
    public static string Mask(string name) =>
        name.Length <= 2 ? $"{name[..1]}*" : $"{name[0]}{new string('*', Math.Min(3, name.Length - 2))}{name[^1]}";

    /// <summary>
    /// Recalculates the seller's summary from their published reviews, copies average + count onto their listings
    /// and returns the event for search and chat. Call after the review change is saved.
    /// </summary>
    public async Task<SellerRatingChanged> RecalculateAsync(string seller, DateTime now)
    {
        var reviews = await db.Reviews.AsNoTracking().Where(r => r.Seller == seller && r.Status == ReviewStatus.Published)
            .Select(r => new { r.SellerRating, r.ItemRating, r.Communication, r.Accuracy, r.Delivery }).ToListAsync();

        var summary = await db.SellerRatings.FindAsync(seller);
        if (summary is null) db.SellerRatings.Add(summary = new SellerRating { Seller = seller });

        static double? Avg(IEnumerable<int?> values)
        {
            var list = values.OfType<int>().ToList();
            return list.Count == 0 ? null : Math.Round(list.Average(), 1);
        }

        summary.Count = reviews.Count;
        summary.Average = reviews.Count == 0 ? 0 : Math.Round(reviews.Average(r => r.SellerRating), 1);
        summary.Stars1 = reviews.Count(r => r.SellerRating == 1);
        summary.Stars2 = reviews.Count(r => r.SellerRating == 2);
        summary.Stars3 = reviews.Count(r => r.SellerRating == 3);
        summary.Stars4 = reviews.Count(r => r.SellerRating == 4);
        summary.Stars5 = reviews.Count(r => r.SellerRating == 5);
        summary.CommunicationAverage = Avg(reviews.Select(r => r.Communication));
        summary.AccuracyAverage = Avg(reviews.Select(r => r.Accuracy));
        summary.DeliveryAverage = Avg(reviews.Select(r => r.Delivery));
        summary.ItemAverage = Avg(reviews.Select(r => (int?)r.ItemRating));
        summary.UpdatedAt = now;
        await db.SaveChangesAsync();

        double? average = summary.Count == 0 ? null : summary.Average;
        await db.Auctions.Where(a => a.Seller == seller)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.SellerRatingAverage, average).SetProperty(a => a.SellerRatingCount, summary.Count));

        return new SellerRatingChanged { Seller = seller, Average = average, Count = summary.Count };
    }
}
