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

    private static DateTime? ParseDate(string value) =>
        string.IsNullOrEmpty(value)
            ? null
            : DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}