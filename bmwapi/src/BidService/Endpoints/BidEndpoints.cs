using System.Security.Claims;
using BidService.Data;
using BidService.DTOs;
using BidService.Services;
using Contracts;

namespace BidService.Endpoints;

public static class BidEndpoints
{
    public static void MapBidEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/bids");

        group.MapPost("/", PlaceBid).RequireAuthorization();
        group.MapPost("/accept", AcceptBid).RequireAuthorization();
        group.MapGet("/steps", GetBidSteps);
        group.MapGet("/mine", GetMyBids).RequireAuthorization();
        group.MapGet("/{auctionId}", GetBidsForAuction);
        group.MapGet("/{auctionId}/next-minimum", GetNextMinimumBid);
        group.MapGet("/{auctionId}/next-min", GetNextMinimumBid); // older clients
    }

    /// <summary>My Bids: the auctions the user bid on, newest first, with Winning / Outbid / Won / Lost.</summary>
    private static async Task<IResult> GetMyBids(ClaimsPrincipal user, IBidRepository repository)
    {
        var me = user.Identity?.Name ?? "";
        var bids = await repository.GetMyBidsAsync(me);
        foreach (var bid in bids) bid.State = BidRules.MyBidState(bid.AuctionStatus, bid.HighBidder, me);
        return Results.Ok(bids);
    }

    /// <summary>The bid step table, e.g. for the seller's review step ("bids go up by 10 below 1,000 ...").</summary>
    private static IResult GetBidSteps() =>
        Results.Ok(BidRules.StepTiers.Select((t, i) => new
        {
            from = t.From,
            to = i + 1 < BidRules.StepTiers.Count ? BidRules.StepTiers[i + 1].From - 1 : (int?)null,
            step = t.Step
        }));

    private static async Task<IResult> PlaceBid(string auctionId, int amount, ClaimsPrincipal user,
        BiddingService biddingService)
    {
        var outcome = await biddingService.PlaceBidAsync(auctionId, user.Identity?.Name ?? "Unknown bidder", amount);

        return ToResult(outcome);
    }

    private static async Task<IResult> AcceptBid(AcceptBidDto dto, ClaimsPrincipal user, BiddingService biddingService)
    {
        // Accepting and closing bids is for seller accounts (BiddingService also checks it's their own auction).
        if (!AppRoles.IsSeller(user))
            return Results.Json(new { message = AppRoles.SellerOnlyMessage, code = AppRoles.SellerOnlyCode }, statusCode: StatusCodes.Status403Forbidden);
        if (string.IsNullOrWhiteSpace(dto.AuctionId) || string.IsNullOrWhiteSpace(dto.BidId))
            return Results.BadRequest("auctionId and bidId are required");

        var outcome = await biddingService.AcceptBidAsync(dto.AuctionId, dto.BidId, user.Identity?.Name ?? "");

        return ToResult(outcome);
    }

    private static async Task<IResult> GetBidsForAuction(string auctionId, IBidRepository repository)
    {
        var bids = await repository.GetBidsForAuctionAsync(auctionId);

        return Results.Ok(bids);
    }

    /// <summary>
    /// The next valid bid and its calculation (current total + step = next), so clients can show
    /// "Current bid $1,000 + step $20 = Next bid $1,020" and pre-fill the bid box.
    /// </summary>
    private static async Task<IResult> GetNextMinimumBid(string auctionId, BiddingService biddingService,
        IBidRepository repository)
    {
        var auction = await biddingService.GetOrFetchAuctionAsync(auctionId);
        if (auction == null) return Results.NotFound();

        var highBid = await repository.GetHighestBidAsync(auctionId);
        var currentTotal = BidRules.CurrentTotal(auction, highBid);
        var step = BidRules.StepFor(currentTotal);
        var minimumBid = BidRules.MinimumBid(auction, highBid);

        return Results.Ok(new NextMinimumBidDto
        {
            MinimumBid = minimumBid,
            CurrentTotal = currentTotal,
            BidStep = step,
            PartPrice = auction.StartingPrice,
            CurrentHighBid = highBid?.Amount,
            BidCount = (await repository.GetBidsForAuctionAsync(auctionId)).Count(),
            HighBidder = highBid?.Bidder,
            QuickBids = BidRules.QuickBids(minimumBid, step),
            BidIncrement = step,
            ReserveMet = BidRules.IsReserveMet(auction, highBid),
            AuctionEnd = auction.AuctionEnd,
            Status = auction.Status.ToString()
        });
    }

    private static IResult ToResult(BidOutcome outcome) => outcome switch
    {
        BidOutcome.Ok ok => Results.Ok(ok.Bid),
        BidOutcome.NotFound notFound => Results.NotFound(notFound.Message),
        BidOutcome.Invalid invalid => Results.BadRequest(invalid.Message),
        BidOutcome.Forbidden => Results.Json(new { message = "Only the seller of this auction can accept bids", code = "NOT_AUCTION_SELLER" }, statusCode: StatusCodes.Status403Forbidden),
        BidOutcome.Conflict conflict => Results.Conflict(new { message = conflict.Message, highBid = conflict.HighBid }),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
    };
}