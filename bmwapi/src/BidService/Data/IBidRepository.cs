using BidService.DTOs;
using BidService.Models;

namespace BidService.Data;

public interface IBidRepository
{
    Task<DbSession> BeginAsync(CancellationToken cancellationToken = default);

    Task<Auction?> GetAuctionAsync(string auctionId);
    /// <summary>Reads the auction row with FOR UPDATE, serializing bids/accepts/ends for that auction.</summary>
    Task<Auction?> LockAuctionAsync(DbSession session, string auctionId);
    /// <summary>Inserts the auction unless it already exists. Returns true if it was inserted.</summary>
    Task<bool> CreateAuctionAsync(Auction auction);
    Task UpdateAuctionTermsAsync(DbSession session, Auction auction);
    Task UpdateAuctionEndAsync(DbSession session, string auctionId, DateTime auctionEnd);
    Task SetStatusAsync(DbSession session, string auctionId, AuctionStatus status, DateTime? decisionDeadline);
    Task DeleteAuctionAsync(string auctionId);

    /// <summary>Highest Accepted / AcceptedBelowReserve bid.</summary>
    Task<Bid?> GetHighestBidAsync(string auctionId, DbSession? session = null);
    Task<bool> HasAcceptedBidsAsync(DbSession session, string auctionId);
    Task<BidDto> InsertBidAsync(DbSession session, Bid bid);
    Task<IEnumerable<BidDto>> GetBidsForAuctionAsync(string auctionId);
}