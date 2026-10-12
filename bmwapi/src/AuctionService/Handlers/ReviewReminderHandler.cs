using AuctionService.Data;
using AuctionService.Services;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Handlers;

/// <summary>Scheduled 2 days after an order is completed (durable, survives restarts).</summary>
public record ReviewReminder(string OrderId);

public class ReviewReminderHandler
{
    /// <returns>ReviewRequested (in-app reminder + push) when the buyer still hasn't reviewed and still can.</returns>
    public async Task<ReviewRequested?> Handle(ReviewReminder message, AuctionDbContext db, TimeProvider clock)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == message.OrderId);
        if (order is null) return null;
        var reviewed = await db.Reviews.AnyAsync(r => r.OrderId == order.Id);
        if (ReviewService.CannotReview(order, order.Buyer, reviewed, clock.GetUtcNow().UtcDateTime) != null) return null;
        return new ReviewRequested { OrderId = order.Id, AuctionId = order.AuctionId, Title = order.Title, Buyer = order.Buyer, Seller = order.Seller };
    }
}
