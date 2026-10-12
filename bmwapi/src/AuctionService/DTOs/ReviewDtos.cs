namespace AuctionService.DTOs;

/// <summary>Writing (POST) or editing (PUT, within 7 days) a review.</summary>
public class SaveReviewDto
{
    /// <summary>POST only: the completed order being reviewed.</summary>
    public string? OrderId { get; set; }
    /// <summary>1–5: the item matched the description.</summary>
    public int ItemRating { get; set; }
    /// <summary>1–5: the seller overall.</summary>
    public int SellerRating { get; set; }
    public int? Communication { get; set; }
    public int? Accuracy { get; set; }
    /// <summary>Pickup / shipping experience.</summary>
    public int? Delivery { get; set; }
    public List<string> Tags { get; set; } = [];
    public string? Comment { get; set; }
    /// <summary>Ids from POST /api/media?purpose=review (the writer's own uploads), at most 5.</summary>
    public List<string> PhotoIds { get; set; } = [];
}

public class ReviewReplyDto
{
    public string Text { get; set; } = "";
}

public class ReviewReportDto
{
    /// <summary>spam, abuse, fake, personal or other.</summary>
    public string Reason { get; set; } = "";
    public string? Details { get; set; }
}

public record ReviewPhotoDto(string Id, string Url, string? ThumbnailUrl);

/// <summary>A review as shown on the seller profile, the listing and the order.</summary>
public class ReviewDto
{
    public required string Id { get; set; }
    public required string OrderId { get; set; }
    public required string ListingId { get; set; }
    public required string ItemTitle { get; set; }
    public string? ItemImageUrl { get; set; }
    /// <summary>The buyer, partly hidden ("a***e"); the full name only for the buyer themselves.</summary>
    public required string Buyer { get; set; }
    public required string Seller { get; set; }
    public int ItemRating { get; set; }
    public int SellerRating { get; set; }
    public int? Communication { get; set; }
    public int? Accuracy { get; set; }
    public int? Delivery { get; set; }
    public List<string> Tags { get; set; } = [];
    public string? Comment { get; set; }
    public List<ReviewPhotoDto> Photos { get; set; } = [];
    public string? SellerReply { get; set; }
    public DateTime? SellerReplyAt { get; set; }
    /// <summary>Published or Hidden (admins and the author see hidden ones).</summary>
    public string Status { get; set; } = "Published";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>The caller wrote it and can still edit it (7 days).</summary>
    public bool CanEdit { get; set; }
    /// <summary>The caller is the seller and hasn't replied yet.</summary>
    public bool CanReply { get; set; }
    public bool IsMine { get; set; }
}

/// <summary>A seller's ratings for the profile and cards.</summary>
public class SellerRatingDto
{
    public required string Seller { get; set; }
    /// <summary>Average seller rating (null: no ratings yet).</summary>
    public double? Average { get; set; }
    public int Count { get; set; }
    /// <summary>Fewer than 3 ratings.</summary>
    public bool IsNewSeller { get; set; }
    /// <summary>Number of 5★, 4★, 3★, 2★, 1★ ratings (in that order).</summary>
    public List<int> Breakdown { get; set; } = [];
    public double? CommunicationAverage { get; set; }
    public double? AccuracyAverage { get; set; }
    public double? DeliveryAverage { get; set; }
    public double? ItemAverage { get; set; }
    /// <summary>Orders the seller completed.</summary>
    public int CompletedSales { get; set; }
    /// <summary>The newest reviews (more with GET api/sellers/:username/reviews).</summary>
    public List<ReviewDto> LatestReviews { get; set; } = [];
}
