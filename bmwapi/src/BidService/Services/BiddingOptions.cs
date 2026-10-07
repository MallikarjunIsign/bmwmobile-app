namespace BidService.Services;

/// <summary>"AntiSniping" section: extend the auction when an accepted bid arrives near the end.</summary>
public class AntiSnipingOptions
{
    public const string SectionName = "AntiSniping";

    public bool Enabled { get; set; }
    /// <summary>A bid within this many minutes of the end triggers an extension.</summary>
    public int WindowMinutes { get; set; } = 2;
    /// <summary>Minutes added to the end time.</summary>
    public int ExtendMinutes { get; set; } = 2;
}

/// <summary>"Auction" section: how auctions end.</summary>
public class AuctionRulesOptions
{
    public const string SectionName = "Auction";

    /// <summary>Whether the seller may accept a high bid that is below the reserve.</summary>
    public bool AllowAcceptBelowReserve { get; set; } = true;
    /// <summary>How long the seller can still accept the high bid after an auction ends below the reserve (fractions allowed, e.g. 0.05 = 3 minutes for testing).</summary>
    public double ReserveGracePeriodHours { get; set; } = 24;
}