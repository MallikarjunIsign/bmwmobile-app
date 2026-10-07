using Npgsql;

namespace BidService.Data;

/// <summary>
/// Hands out pooled connections from one NpgsqlDataSource. (A single shared NpgsqlConnection is not safe
/// for concurrent requests.)
/// </summary>
public class BidDbContext(NpgsqlDataSource dataSource)
{
    public NpgsqlConnection CreateConnection() => dataSource.CreateConnection();

    public async Task<DbSession> BeginAsync(CancellationToken cancellationToken = default)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var transaction = await connection.BeginTransactionAsync(cancellationToken);
        return new DbSession(connection, transaction);
    }
}

/// <summary>An open connection with a transaction; disposing rolls back unless committed.</summary>
public sealed class DbSession(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
{
    public NpgsqlConnection Connection { get; } = connection;
    public NpgsqlTransaction Transaction { get; } = transaction;

    public Task CommitAsync(CancellationToken cancellationToken = default) => Transaction.CommitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await Transaction.DisposeAsync();
        await Connection.DisposeAsync();
    }
}