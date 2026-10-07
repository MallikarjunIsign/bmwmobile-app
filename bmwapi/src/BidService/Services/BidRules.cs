using System.Globalization;
using BidService.Models;

namespace BidService.Services;

/// <summary>Pure bidding rules (no I/O), shared by the endpoints and handlers and unit-tested directly.</summary>
public static class BidRules
{
    /// <summary>Starting price before the first accepted bid, then current high bid + increment.</summary>
    public static int MinimumBid(Auction auction, Bid? highBid) =>
        highBid is null ? auction.StartingPrice : highBid.Amount + auction.BidIncrement;

    public static string MinimumBidMessage(int minimumBid) =>
        $"Minimum bid is {minimumBid.ToString("N0", CultureInfo.InvariantCulture)}";

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