using BidService.DTOs;
using BidService.Models;
using Dapper;
using Mapster;

namespace BidService.Data;

public class BidRepository(BidDbContext dbContext) : IBidRepository
{
    private static readonly object AcceptedStatuses = new
    {
        Accepted = (int)BidStatus.Accepted,
        AcceptedBelowReserve = (int)BidStatus.AcceptedBelowReserve
    };

    public Task<DbSession> BeginAsync(CancellationToken cancellationToken = default) =>
        dbContext.BeginAsync(cancellationToken);

    public async Task<Auction?> GetAuctionAsync(string auctionId)
    {
        await using var connection = dbContext.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Auction>(
            "select * from auctions where id = @auctionId", new { auctionId }
        );
    }

    public async Task<Auction?> LockAuctionAsync(DbSession session, string auctionId)
    {
        return await session.Connection.QuerySingleOrDefaultAsync<Auction>(
            "select * from auctions where id = @auctionId for update", new { auctionId }, session.Transaction
        );
    }

    public async Task<bool> CreateAuctionAsync(Auction auction)
    {
        await using var connection = dbContext.CreateConnection();
        var inserted = await connection.ExecuteAsync(
            """
            insert into auctions (id, auctionend, seller, reserveprice, startingprice, bidincrement, status,
                                  decisiondeadline, finished)
            values (@Id, @AuctionEnd, @Seller, @ReservePrice, @StartingPrice, @BidIncrement, @Status,
                    @DecisionDeadline, @Finished)
            on conflict (id) do nothing
            """, auction
        );

        return inserted > 0;
    }

    public async Task UpdateAuctionTermsAsync(DbSession session, Auction auction)
    {
        await session.Connection.ExecuteAsync(
            """
            update auctions
            set auctionend = @AuctionEnd, reserveprice = @ReservePrice, startingprice = @StartingPrice,
                bidincrement = @BidIncrement
            where id = @Id
            """, auction, session.Transaction
        );
    }

    public async Task UpdateAuctionEndAsync(DbSession session, string auctionId, DateTime auctionEnd)
    {
        await session.Connection.ExecuteAsync(
            "update auctions set auctionend = @auctionEnd where id = @auctionId",
            new { auctionId, auctionEnd }, session.Transaction
        );
    }

    public async Task SetStatusAsync(DbSession session, string auctionId, AuctionStatus status, DateTime? decisionDeadline)
    {
        await session.Connection.ExecuteAsync(
            """
            update auctions
            set status = @status, decisiondeadline = @decisionDeadline, finished = @finished
            where id = @auctionId
            """,
            new { auctionId, status = (int)status, decisionDeadline, finished = status != AuctionStatus.Live },
            session.Transaction
        );
    }

    public async Task DeleteAuctionAsync(string auctionId)
    {
        await using var connection = dbContext.CreateConnection();
        await connection.ExecuteAsync(
            """
            delete from bids where auctionid = @auctionId;
            delete from auctions where id = @auctionId;
            """, new { auctionId }
        );
    }

    public async Task<Bid?> GetHighestBidAsync(string auctionId, DbSession? session = null)
    {
        const string sql = """
            select * from bids
            where auctionid = @auctionId and bidstatus in (@Accepted, @AcceptedBelowReserve)
            order by amount desc, bidtime asc
            limit 1
            """;
        var args = new DynamicParameters(AcceptedStatuses);
        args.Add("auctionId", auctionId);

        if (session != null)
            return await session.Connection.QuerySingleOrDefaultAsync<Bid>(sql, args, session.Transaction);

        await using var connection = dbContext.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Bid>(sql, args);
    }

    public async Task<bool> HasAcceptedBidsAsync(DbSession session, string auctionId) =>
        await GetHighestBidAsync(auctionId, session) != null;

    public async Task<BidDto> InsertBidAsync(DbSession session, Bid bid)
    {
        await session.Connection.ExecuteAsync(
            """
            insert into bids (id, auctionid, bidder, bidtime, amount, bidstatus)
            values (@Id, @AuctionId, @Bidder, @BidTime, @Amount, @BidStatus)
            """, bid, session.Transaction
        );
        return bid.Adapt<BidDto>();
    }

    public async Task<IEnumerable<BidDto>> GetBidsForAuctionAsync(string auctionId)
    {
        await using var connection = dbContext.CreateConnection();
        var bids = await connection.QueryAsync<Bid>(
            """
            select * from bids
            where auctionid = @auctionId
            order by bidtime desc
            """, new { auctionId }
        );

        return bids.Adapt<IEnumerable<BidDto>>();
    }
}