using Contracts;

namespace AuctionService.Entities;

public static class AuctionStatusRules
{
    /// <summary>
    /// Live can move to any end status; ReserveNotMet (waiting for the seller) can only move to
    /// Sold or Unsold. Everything else (including repeats of the same status) is ignored, which makes
    /// AuctionFinished handling idempotent.
    /// </summary>
    public static bool CanTransition(Status current, Status next) => current switch
    {
        Status.Live => next != Status.Live,
        Status.ReserveNotMet => next is Status.Sold or Status.Unsold,
        _ => false
    };

    /// <summary>Status carried by the message, or derived from ItemSold for older messages without one.</summary>
    public static Status ResolveStatus(AuctionFinished message) =>
        Enum.TryParse<Status>(message.Status, out var status) && status != Status.Live
            ? status
            : message.ItemSold ? Status.Sold : Status.Unsold;

    public static EndReason ResolveEndReason(AuctionFinished message) =>
        Enum.TryParse<EndReason>(message.EndReason, out var reason) ? reason : EndReason.TimeEnded;
}