using System.Data.Common;
using Wolverine;
using Wolverine.RDBMS;
using Wolverine.Runtime;

namespace BidService.Services;

/// <summary>
/// Wolverine outbox for raw ADO.NET/Dapper transactions: messages published after enlisting are stored in the
/// same Postgres transaction and only sent once it commits (then flushed).
/// </summary>
public static class OutboxExtensions
{
    public static async Task EnlistInOutboxAsync(this IMessageContext bus, DbTransaction transaction)
    {
        if (bus is not MessageContext context)
            throw new InvalidOperationException($"Expected a Wolverine MessageContext, got {bus.GetType().Name}");

        if (context.Runtime.Storage is not IMessageDatabase database)
            throw new InvalidOperationException("Wolverine message persistence must be a database (PersistMessagesWithPostgresql)");

        await context.EnlistInOutboxAsync(new DatabaseEnvelopeTransaction(database, transaction));
    }

    public static async Task FlushOutgoingMessagesAsync(this IMessageContext bus)
    {
        if (bus is MessageContext context) await context.FlushOutgoingMessagesAsync();
    }
}