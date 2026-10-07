using Contracts;
using Meilisearch;
using SearchService.Data;
using SearchService.Models;

namespace SearchService.Handlers;

public class AuctionFinishedHandler(ILogger<AuctionFinishedHandler> logger)
{
    private static readonly string[] EndStatuses = ["Sold", "ReserveNotMet", "Unsold"];

    /// <summary>
    /// Applies the end status idempotently: Live can move to any end status, ReserveNotMet only to Sold or
    /// Unsold. Repeated or late messages are ignored.
    /// </summary>
    public async Task Handle(AuctionFinished message, MeilisearchClient client)
    {
        var index = client.Auctions();
        var auction = await index.GetDocumentAsync<Item>(message.AuctionId)
            ?? throw new InvalidOperationException("Could not find auction");

        var status = ResolveStatus(message);

        if (!CanTransition(auction.Status, status))
        {
            logger.LogInformation("Ignoring AuctionFinished for {AuctionId}: {Current} -> {Next}",
                auction.Id, auction.Status, status);
            return;
        }

        auction.Status = status;
        auction.EndReason = message.EndReason ?? "TimeEnded";
        auction.SellerDecisionDeadline = status == "ReserveNotMet" ? message.SellerDecisionDeadline : null;

        if (status == "Sold")
        {
            auction.Winner = message.Winner;
            auction.SoldAmount = message.Amount;
        }

        var updateTask = await index.UpdateDocumentsAsync([auction]);
        await client.WaitForTaskAsync(updateTask.TaskUid);
    }

    public static string ResolveStatus(AuctionFinished message) =>
        message.Status is { } status && EndStatuses.Contains(status)
            ? status
            : message.ItemSold ? "Sold" : "Unsold";

    // Sold, Unsold (and the pre-upgrade "Finished") are final.
    public static bool CanTransition(string current, string next) => current switch
    {
        "Live" => true,
        "ReserveNotMet" => next is "Sold" or "Unsold",
        _ => false
    };
}