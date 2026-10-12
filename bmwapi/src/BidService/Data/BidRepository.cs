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

    public async Task<List<string>> GetBiddersAsync(string auctionId, DbSession? session = null)
    {
        const string sql = """
            select distinct bidder from bids
            where auctionid = @auctionId and bidstatus in (@Accepted, @AcceptedBelowReserve)
            """;
        var args = new DynamicParameters(AcceptedStatuses);
        args.Add("auctionId", auctionId);

        if (session != null)
            return (await session.Connection.QueryAsync<string>(sql, args, session.Transaction)).ToList();

        await using var connection = dbContext.CreateConnection();
        return (await connection.QueryAsync<string>(sql, args)).ToList();
    }

    public async Task<int> CountAcceptedBidsAsync(string auctionId, DbSession? session = null)
    {
        const string sql = """
            select count(*) from bids
            where auctionid = @auctionId and bidstatus in (@Accepted, @AcceptedBelowReserve)
            """;
        var args = new DynamicParameters(AcceptedStatuses);
        args.Add("auctionId", auctionId);

        if (session != null)
            return await session.Connection.ExecuteScalarAsync<int>(sql, args, session.Transaction);

        await using var connection = dbContext.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(sql, args);
    }

    public async Task<List<MyBidDto>> GetMyBidsAsync(string bidder)
    {
        await using var connection = dbContext.CreateConnection();
        var args = new DynamicParameters(AcceptedStatuses);
        args.Add("bidder", bidder);
        var rows = await connection.QueryAsync<MyBidRow>(
            """
            with mine as (
                select auctionid, max(amount) as myhighest, max(bidtime) as mylast
                from bids
                where bidder = @bidder and bidstatus in (@Accepted, @AcceptedBelowReserve)
                group by auctionid
            ), top as (
                select distinct on (auctionid) auctionid, amount, bidder
                from bids
                where bidstatus in (@Accepted, @AcceptedBelowReserve) and auctionid in (select auctionid from mine)
                order by auctionid, amount desc, bidtime asc
            ), counts as (
                select auctionid, count(*) as bidcount
                from bids
                where bidstatus in (@Accepted, @AcceptedBelowReserve) and auctionid in (select auctionid from mine)
                group by auctionid
            )
            select m.auctionid, m.myhighest, m.mylast, t.amount as currenthighbid, t.bidder as highbidder,
                   c.bidcount, a.status, a.auctionend
            from mine m
            join auctions a on a.id = m.auctionid
            left join top t on t.auctionid = m.auctionid
            left join counts c on c.auctionid = m.auctionid
            order by m.mylast desc
            """, args);

        return rows.Select(r => new MyBidDto
        {
            AuctionId = r.AuctionId,
            MyHighestBid = r.MyHighest,
            MyLastBidTime = DateTime.SpecifyKind(r.MyLast, DateTimeKind.Utc),
            CurrentHighBid = r.CurrentHighBid,
            HighBidder = r.HighBidder,
            BidCount = (int)r.BidCount,
            AuctionStatus = ((AuctionStatus)r.Status).ToString(),
            AuctionEnd = DateTime.SpecifyKind(r.AuctionEnd, DateTimeKind.Utc)
        }).ToList();
    }

    private sealed class MyBidRow
    {
        public string AuctionId { get; set; } = "";
        public int MyHighest { get; set; }
        public DateTime MyLast { get; set; }
        public int? CurrentHighBid { get; set; }
        public string? HighBidder { get; set; }
        public long BidCount { get; set; }
        public int Status { get; set; }
        public DateTime AuctionEnd { get; set; }
    }

    public async Task SetBlockedAsync(string blocker, string blocked, bool isBlocked)
    {
        await using var connection = dbContext.CreateConnection();
        await connection.ExecuteAsync(isBlocked
            ? "insert into userblocks (blocker, blocked) values (@blocker, @blocked) on conflict do nothing"
            : "delete from userblocks where blocker = @blocker and blocked = @blocked",
            new { blocker, blocked });
    }

    public async Task<bool> IsBlockedAsync(string blocker, string blocked)
    {
        await using var connection = dbContext.CreateConnection();
        return await connection.ExecuteScalarAsync<bool>(
            "select exists (select 1 from userblocks where blocker = @blocker and blocked = @blocked)", new { blocker, blocked });
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