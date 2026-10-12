namespace BidService.DTOs;

/// <summary>One auction in "My Bids".</summary>
public class MyBidDto
{
    public required string AuctionId { get; set; }
    public int MyHighestBid { get; set; }
    public DateTime MyLastBidTime { get; set; }
    public int? CurrentHighBid { get; set; }
    public string? HighBidder { get; set; }
    public int BidCount { get; set; }
    public required string AuctionStatus { get; set; }
    public DateTime AuctionEnd { get; set; }
    /// <summary>Winning / Outbid while Live; Won / Lost once ended.</summary>
    public string State { get; set; } = "";
}

public class AcceptBidDto
{
    public string AuctionId { get; set; } = "";
    public string BidId { get; set; } = "";
}

/// <summary>
/// The next valid bid and how it is worked out: "Current bid 1,000 + step 20 = Next bid 1,020".
/// Amounts are totals (part price + bid) in the listing's currency units.
/// </summary>
public class NextMinimumBidDto
{
    /// <summary>The next valid bid total (CurrentTotal + BidStep).</summary>
    public int MinimumBid { get; set; }
    /// <summary>The highest bid total, or the part price when there are no bids yet.</summary>
    public int CurrentTotal { get; set; }
    /// <summary>The step from the bid step table for CurrentTotal.</summary>
    public int BidStep { get; set; }
    /// <summary>The seller's part price (the base the bids are added on top of).</summary>
    public int PartPrice { get; set; }
    public int? CurrentHighBid { get; set; }
    public int BidCount { get; set; }
    public string? HighBidder { get; set; }
    /// <summary>Next, next + 1 step, next + 2 steps, next + 5 steps.</summary>
    public IReadOnlyList<int> QuickBids { get; set; } = [];
    /// <summary>Same as BidStep (kept for older clients).</summary>
    public int BidIncrement { get; set; }
    public bool ReserveMet { get; set; }
    public DateTime AuctionEnd { get; set; }
    public required string Status { get; set; }
}