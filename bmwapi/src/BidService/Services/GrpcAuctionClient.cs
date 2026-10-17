using System.Globalization;
using BidService.Models;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcAuctionService;

namespace BidService.Services;

public class GrpcAuctionClient(IConfiguration config)
{
    public virtual Auction? GetAuction(string auctionId)
    {
        using var channel = GrpcChannel.ForAddress(config["GrpcAuctionServiceUrl"]!);
        var client = new GrpcAuction.GrpcAuctionClient(channel);

        try
        {
            var reply = client.GetAuction(new GetAuctionRequest{AuctionId = auctionId});
            var model = reply.Auction;

            // Older AuctionService versions don't send the new fields: fall back to "finished" and safe defaults.
            var status = Enum.TryParse<AuctionStatus>(model.Status, out var parsed)
                ? parsed
                : model.Finished ? AuctionStatus.Unsold : AuctionStatus.Live;
            var startingPrice = Math.Max(model.StartingPrice, 1);

            return new Auction
            {
                Id = model.Id,
                AuctionEnd = ParseDate(model.AuctionEnd)!.Value,
                Seller = model.Seller,
                StartingPrice = startingPrice,
                ReservePrice = Math.Max(model.ReservePrice, startingPrice),
                BidIncrement = Math.Max(model.BidIncrement, 1),
                Status = status,
                DecisionDeadline = status == AuctionStatus.ReserveNotMet ? ParseDate(model.SellerDecisionDeadline) : null,
                Finished = status != AuctionStatus.Live,
            };
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Why this bidder may not bid (non-payment strikes in AuctionService), or null. AuctionService not configured or
    /// unreachable: null (bidding isn't blocked because of an outage; logged).
    /// </summary>
    public virtual string? BiddingBlockedReason(string bidder, ILogger? logger = null)
    {
        var url = config["GrpcAuctionServiceUrl"];
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            using var channel = GrpcChannel.ForAddress(url);
            var reply = new GrpcAuction.GrpcAuctionClient(channel)
                .GetBidderStanding(new BidderStandingRequest { Bidder = bidder }, deadline: DateTime.UtcNow.AddSeconds(5));
            return string.IsNullOrEmpty(reply.BlockedReason) ? null : reply.BlockedReason;
        }
        catch (RpcException e)
        {
            logger?.LogWarning("Bidder standing not checked ({Status}): AuctionService not reachable", e.StatusCode);
            return null;
        }
    }

    private static DateTime? ParseDate(string value) =>
        string.IsNullOrEmpty(value)
            ? null
            : DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}