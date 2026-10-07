using System.Security.Claims;
using BidService.Data;
using BidService.DTOs;
using BidService.Services;

namespace BidService.Endpoints;

public static class BidEndpoints
{
    public static void MapBidEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/bids");

        group.MapPost("/", PlaceBid).RequireAuthorization();
        group.MapPost("/accept", AcceptBid).RequireAuthorization();
        group.MapGet("/{auctionId}", GetBidsForAuction);
        group.MapGet("/{auctionId}/next-min", GetNextMinimumBid);
    }

    private static async Task<IResult> PlaceBid(string auctionId, int amount, ClaimsPrincipal user,
        BiddingService biddingService)
    {
        var outcome = await biddingService.PlaceBidAsync(auctionId, user.Identity?.Name ?? "Unknown bidder", amount);

        return ToResult(outcome);
    }

    private static async Task<IResult> AcceptBid(AcceptBidDto dto, ClaimsPrincipal user, BiddingService biddingService)
    {
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

    /// <summary>What the next bid must be at least, so clients can pre-fill the bid box.</summary>
    private static async Task<IResult> GetNextMinimumBid(string auctionId, BiddingService biddingService,
        IBidRepository repository)
    {
        var auction = await biddingService.GetOrFetchAuctionAsync(auctionId);
        if (auction == null) return Results.NotFound();

        var highBid = await repository.GetHighestBidAsync(auctionId);

        return Results.Ok(new NextMinimumBidDto
        {
            MinimumBid = BidRules.MinimumBid(auction, highBid),
            CurrentHighBid = highBid?.Amount,
            BidIncrement = auction.BidIncrement,
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
        BidOutcome.Forbidden => Results.Forbid(),
        BidOutcome.Conflict conflict => Results.Conflict(new { message = conflict.Message, highBid = conflict.HighBid }),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
    };
}