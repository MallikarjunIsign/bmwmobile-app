using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuctionService.Entities;

public enum ReviewStatus
{
    Published = 0,
    /// <summary>Hidden by an admin (after a report): not shown and not counted.</summary>
    Hidden = 1
}

/// <summary>
/// A verified-purchase review: only the buyer of a completed order, once per order. Rates the item (matched the
/// description) and the seller (overall + optional communication / accuracy / delivery), with optional tags, comment
/// and up to 5 photos. The seller may post one public reply.
/// </summary>
[Table("Reviews")]
public class Review
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [MaxLength(36)]
    public required string OrderId { get; set; }
    [MaxLength(36)]
    public required string ListingId { get; set; }
    [MaxLength(100)]
    public required string Buyer { get; set; }
    [MaxLength(100)]
    public required string Seller { get; set; }

    /// <summary>The item as sold (shown with the review on the seller's profile).</summary>
    [MaxLength(250)]
    public required string ItemTitle { get; set; }
    [MaxLength(500)]
    public string? ItemImageUrl { get; set; }

    /// <summary>1–5: the item matched the description.</summary>
    public int ItemRating { get; set; }
    /// <summary>1–5: the seller overall.</summary>
    public int SellerRating { get; set; }
    /// <summary>Optional 1–5 quick ratings.</summary>
    public int? Communication { get; set; }
    public int? Accuracy { get; set; }
    /// <summary>Pickup / shipping experience.</summary>
    public int? Delivery { get; set; }

    public List<string> Tags { get; set; } = [];
    [MaxLength(1000)]
    public string? Comment { get; set; }
    public List<ReviewPhoto> Photos { get; set; } = [];

    [MaxLength(1000)]
    public string? SellerReply { get; set; }
    public DateTime? SellerReplyAt { get; set; }

    public ReviewStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A review photo (uploaded with /api/media?purpose=review by the buyer).</summary>
public class ReviewPhoto
{
    public string MediaId { get; set; } = "";
    public string Url { get; set; } = "";
    public string? ThumbnailUrl { get; set; }
}

/// <summary>"Report review": anyone signed in; an admin can hide the review.</summary>
[Table("ReviewReports")]
public class ReviewReport
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [MaxLength(36)]
    public required string ReviewId { get; set; }
    [MaxLength(100)]
    public required string Reporter { get; set; }
    /// <summary>spam, abuse, fake, personal (personal details), other.</summary>
    [MaxLength(20)]
    public required string Reason { get; set; }
    [MaxLength(500)]
    public string? Details { get; set; }
    /// <summary>Open or Closed.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "Open";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A seller's ratings, recalculated from their published reviews whenever one changes: average + count, the 5★…1★
/// breakdown, the quick-rating averages, and the completed sales.
/// </summary>
[Table("SellerRatings")]
public class SellerRating
{
    [Key, MaxLength(100)]
    public required string Seller { get; set; }
    public double Average { get; set; }
    public int Count { get; set; }
    public int Stars1 { get; set; }
    public int Stars2 { get; set; }
    public int Stars3 { get; set; }
    public int Stars4 { get; set; }
    public int Stars5 { get; set; }
    public double? CommunicationAverage { get; set; }
    public double? AccuracyAverage { get; set; }
    public double? DeliveryAverage { get; set; }
    /// <summary>Average item rating ("matched the description").</summary>
    public double? ItemAverage { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
