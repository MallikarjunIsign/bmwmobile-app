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

    /// <summary>Distinct usernames that placed accepted bids on the auction.</summary>
    Task<List<string>> GetBiddersAsync(string auctionId, DbSession? session = null);
    Task<int> CountAcceptedBidsAsync(string auctionId, DbSession? session = null);
    /// <summary>"My Bids": one row per auction the user bid on, with their highest bid and the auction's state.</summary>
    Task<List<MyBidDto>> GetMyBidsAsync(string bidder);

    /// <summary>Blocks copied from ChatService: <paramref name="blocked"/> can't bid on <paramref name="blocker"/>'s listings.</summary>
    Task SetBlockedAsync(string blocker, string blocked, bool isBlocked);

    Task<bool> IsBlockedAsync(string blocker, string blocked);
}