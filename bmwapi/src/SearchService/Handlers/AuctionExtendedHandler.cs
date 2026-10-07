using Contracts;
using Meilisearch;
using SearchService.Data;
using SearchService.Models;

namespace SearchService.Handlers;

public class AuctionExtendedHandler
{
    public async Task Handle(AuctionExtended message, MeilisearchClient client)
    {
        var index = client.Auctions();
        var auction = await index.GetDocumentAsync<Item>(message.AuctionId)
            ?? throw new InvalidOperationException("Could not find auction");

        if (message.AuctionEnd <= auction.AuctionEnd) return;

        auction.AuctionEnd = message.AuctionEnd;

        var updateTask = await index.UpdateDocumentsAsync([auction]);
        await client.WaitForTaskAsync(updateTask.TaskUid);
    }
}

public class ContainerUpdatedHandler
{
    /// <summary>Copies the container's new details (status, dates, route) into every auction shipped in it.</summary>
    public async Task Handle(ContainerUpdated message, MeilisearchClient client)
    {
        if (message.AuctionIds.Count == 0) return;

        var container = new ContainerInfo
        {
            Id = message.Id,
            ContainerNumber = message.ContainerNumber,
            OriginPort = message.OriginPort,
            OriginCountry = message.OriginCountry,
            DestinationPort = message.DestinationPort,
            DestinationCountry = message.DestinationCountry,
            DepartureDate = message.DepartureDate,
            EstimatedArrivalDate = message.EstimatedArrivalDate,
            ShippingStatus = message.ShippingStatus
        };

        // Partial update: only the "container" field of each document is replaced.
        var documents = message.AuctionIds.Select(id => new ContainerPatch(id, container)).ToList();

        var updateTask = await client.Auctions().UpdateDocumentsAsync(documents, primaryKey: "id");
        await client.WaitForTaskAsync(updateTask.TaskUid);
    }

    private record ContainerPatch(string Id, ContainerInfo Container);
}