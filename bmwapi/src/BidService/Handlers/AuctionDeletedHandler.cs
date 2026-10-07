using BidService.Data;
using Contracts;

namespace BidService.Handlers;

public class AuctionDeletedHandler(IBidRepository repository)
{
    // The scheduled end check then finds nothing and does nothing.
    public Task Handle(AuctionDeleted message) => repository.DeleteAuctionAsync(message.Id);
}