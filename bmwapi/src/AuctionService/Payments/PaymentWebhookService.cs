using AuctionService.Data;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Payments;

/// <summary>
/// Webhooks from the provider, after the signature check. Each event is handled once (its id is stored); events about
/// objects that aren't tagged app=shopcarparts (another project / Stripe's own sample objects) are ignored. Disputes carry
/// no metadata: they count as ours when the disputed charge is one of our payments.
/// An order becomes Paid only here (payment_intent.succeeded) – never because the app says so.
/// </summary>
public class PaymentWebhookService(AuctionDbContext db, SellerPaymentService sellers, CheckoutService checkout,
    SellerPayoutService payouts, ILogger<PaymentWebhookService> logger)
{
    public async Task<string> HandleAsync(PaymentWebhookEvent evt, CancellationToken ct)
    {
        var ours = evt.OwnedByApp ||
                   evt.Dispute is { } d && await payouts.IsOursAsync(d.ChargeId, d.PaymentIntentId, ct);
        if (!ours) return "ignored: not this app's object";
        if (await db.ProcessedWebhookEvents.AsNoTracking().AnyAsync(e => e.EventId == evt.Id, ct)) return "duplicate";

        var outcome = evt.Type switch
        {
            "account.updated" when evt.Account is not null =>
                await sellers.SyncAsync(evt.Account, ct) ? "seller account synced" : "unknown account",
            "payment_intent.succeeded" when evt.Payment is not null => await checkout.HandleSucceededAsync(evt.Payment, ct),
            "payment_intent.payment_failed" when evt.Payment is not null => await checkout.HandleFailedAsync(evt.Payment, ct),
            "payment_intent.canceled" when evt.Payment is not null => await checkout.HandleCanceledAsync(evt.Payment, ct),
            "charge.refunded" when evt.Charge is not null => await payouts.HandleChargeRefundedAsync(evt.Charge, ct),
            "charge.dispute.created" or "charge.dispute.updated" or "charge.dispute.closed" when evt.Dispute is not null =>
                await payouts.HandleDisputeAsync(evt.Type, evt.Dispute, ct),
            "transfer.reversed" or "transfer.updated" when evt.Transfer is not null => await payouts.HandleTransferAsync(evt.Transfer, ct),
            _ => "not handled",
        };

        db.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent { EventId = evt.Id, Type = evt.Type, Outcome = outcome.Length > 200 ? outcome[..200] : outcome });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The same event delivered twice at once: the other request handled it.
            if (await db.ProcessedWebhookEvents.AsNoTracking().AnyAsync(e => e.EventId == evt.Id, ct)) return "duplicate";
            throw;
        }
        logger.LogInformation("Webhook {EventId} {Type}: {Outcome}", evt.Id, evt.Type, outcome);
        return outcome;
    }
}
