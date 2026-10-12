using System.Globalization;
using BidService.Models;

namespace BidService.Services;

/// <summary>One row of the bid step table: from this current total (inclusive) bids go up by Step.</summary>
public record BidStepTier(int From, int Step);

/// <summary>Pure bidding rules (no I/O), shared by the endpoints and handlers and unit-tested directly.</summary>
public static class BidRules
{
    /// <summary>
    /// The bid step table: the ONLY place it is defined. The step depends on the current total (part price +
    /// highest bid, i.e. the highest bid amount, or the part price before the first bid), in the listing's
    /// currency units. Exposed to the app through GET /api/bids/steps and /api/bids/{id}/next-minimum.
    /// </summary>
    public static readonly IReadOnlyList<BidStepTier> StepTiers =
    [
        new(0, 10),
        new(1_000, 20),
        new(5_000, 50),
        new(10_000, 100),
        new(50_000, 250),
    ];

    /// <summary>How much the next bid must add to <paramref name="currentTotal"/>.</summary>
    public static int StepFor(int currentTotal) =>
        (StepTiers.LastOrDefault(t => currentTotal >= t.From) ?? StepTiers[0]).Step;

    /// <summary>The current total: the highest bid (bids are stored as totals), or the part price before the first bid.</summary>
    public static int CurrentTotal(Auction auction, Bid? highBid) => highBid?.Amount ?? auction.StartingPrice;

    /// <summary>The next valid bid total: current total + the step for that total (e.g. 1,000 → 1,020).</summary>
    public static int MinimumBid(Auction auction, Bid? highBid)
    {
        var current = CurrentTotal(auction, highBid);
        return current + StepFor(current);
    }

    /// <summary>Quick-bid amounts: next, next + 1 step, next + 2 steps, next + 5 steps.</summary>
    public static IReadOnlyList<int> QuickBids(int minimumBid, int step) =>
        [minimumBid, minimumBid + step, minimumBid + 2 * step, minimumBid + 5 * step];

    public static string MinimumBidMessage(int minimumBid) =>
        $"Your bid is too low. The next valid bid is {minimumBid.ToString("N0", CultureInfo.InvariantCulture)}";

    public const string OwnListingMessage = "You can't bid on your own listing";
    public const string BlockedMessage = "You can't bid on this seller's listings";

    public const string AlreadyHighestMessage = "You're the highest bidder";

    /// <summary>My Bids: Winning / Outbid while Live; Won / Lost once ended.</summary>
    public static string MyBidState(string status, string? highBidder, string me) => status switch
    {
        nameof(AuctionStatus.Live) => highBidder == me ? "Winning" : "Outbid",
        nameof(AuctionStatus.Sold) => highBidder == me ? "Won" : "Lost",
        _ => "Lost"
    };

    public static BidStatus StatusForAcceptedBid(Auction auction, int amount) =>
        amount >= auction.ReservePrice ? BidStatus.Accepted : BidStatus.AcceptedBelowReserve;

    public static bool IsReserveMet(Auction auction, Bid? highBid) =>
        highBid is not null && highBid.Amount >= auction.ReservePrice;

    /// <summary>Bids are only taken while the auction is Live and its end time hasn't passed.</summary>
    public static bool IsOpenForBids(Auction auction, DateTime now) =>
        auction.Status == AuctionStatus.Live && auction.AuctionEnd > now;

    /// <summary>New end time when anti-sniping applies to a bid placed at <paramref name="now"/>, otherwise null.</summary>
    public static DateTime? ExtendedEnd(Auction auction, DateTime now, AntiSnipingOptions options)
    {
        if (!options.Enabled || options.ExtendMinutes <= 0) return null;

        var remaining = auction.AuctionEnd - now;
        if (remaining <= TimeSpan.Zero || remaining > TimeSpan.FromMinutes(options.WindowMinutes)) return null;

        return auction.AuctionEnd.AddMinutes(options.ExtendMinutes);
    }

    /// <summary>Outcome when the time runs out.</summary>
    public static AuctionStatus StatusWhenTimeEnds(Auction auction, Bid? highBid) => highBid switch
    {
        null => AuctionStatus.Unsold,
        _ when highBid.Amount >= auction.ReservePrice => AuctionStatus.Sold,
        _ => AuctionStatus.ReserveNotMet
    };

    /// <summary>
    /// Whether the seller may accept now: while Live (before the end), or during the ReserveNotMet grace period.
    /// Returns the end reason to record, or an error message.
    /// </summary>
    public static (EndReason? Reason, string? Error) CanSellerAccept(Auction auction, DateTime now) => auction.Status switch
    {
        AuctionStatus.Live when auction.AuctionEnd > now => (EndReason.SellerAccepted, null),
        AuctionStatus.Live => (null, "The auction is ending, please try again in a moment"),
        AuctionStatus.ReserveNotMet when auction.DecisionDeadline is null || auction.DecisionDeadline > now
            => (EndReason.SellerAcceptedAfterEnd, null),
        AuctionStatus.ReserveNotMet => (null, "The time to accept the highest bid has passed"),
        _ => (null, "The auction has already finished")
    };
}