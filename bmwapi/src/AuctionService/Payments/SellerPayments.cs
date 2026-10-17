using AuctionService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AuctionService.Payments;

/// <summary>
/// Sellers' payout accounts: created when an admin makes someone a seller (IdentityService → internal endpoint), or on
/// the seller's first visit to Payments; kept in sync from the provider's account.updated webhook.
/// </summary>
public class SellerPaymentService(AuctionDbContext db, IPaymentProvider provider, IOptions<PaymentOptions> options,
    ILogger<SellerPaymentService> logger)
{
    private readonly PaymentOptions _opt = options.Value;

    /// <summary>Online payment is on (keys set) and the country is one of Payments:Countries (US, IE).</summary>
    public bool IsOnline(string? country) => provider.IsConfigured && _opt.IsOnlineCountry(country);

    public Task<SellerPaymentAccount?> FindAsync(string seller, CancellationToken ct) =>
        db.SellerPaymentAccounts.FirstOrDefaultAsync(a => a.Seller == seller, ct);

    /// <summary>
    /// The seller's account. When this database has none, an account the seller already has at the provider (made while
    /// another database was in use) is linked; only when there is none is a new one created. Null when payments aren't
    /// online there.
    /// </summary>
    public async Task<SellerPaymentAccount?> EnsureAsync(string seller, string? country, string? email, string? displayName, CancellationToken ct)
    {
        var existing = await FindAsync(seller, ct);
        if (existing is not null) return await ReconcileAsync(seller, existing, ct);
        if (!IsOnline(country)) return null;

        if (await ReconcileAsync(seller, null, ct) is { } linked) return linked;

        var snapshot = await provider.CreateSellerAccountAsync(new NewSellerAccount(seller, country!.Trim().ToUpperInvariant(), email, displayName), ct);
        var account = new SellerPaymentAccount
        {
            Seller = seller,
            Provider = provider.Name,
            ProviderAccountId = snapshot.AccountId,
            Country = snapshot.Country.Length == 2 ? snapshot.Country : country.Trim().ToUpperInvariant(),
        };
        Apply(account, snapshot);
        db.SellerPaymentAccounts.Add(account);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Created at the same moment by another request (admin + first visit): keep the stored one.
            db.Entry(account).State = EntityState.Detached;
            if (!await db.SellerPaymentAccounts.AsNoTracking().AnyAsync(a => a.Seller == seller, ct)) throw;
            logger.LogWarning("Seller {Seller} got two payment accounts at once; {AccountId} is not used", seller, snapshot.AccountId);
            return await FindAsync(seller, ct);
        }
        return account;
    }

    /// <summary>Reads the account from the provider again (after onboarding, when the app opens Payments).</summary>
    public async Task<SellerPaymentAccount> RefreshAsync(SellerPaymentAccount account, CancellationToken ct)
    {
        Apply(account, await provider.GetSellerAccountAsync(account.ProviderAccountId, ct));
        await db.SaveChangesAsync(ct);
        return account;
    }

    /// <summary>
    /// Until the stored account is Active: links the seller's best account at the provider instead, when there is a
    /// better one (finished on the other database) or none is stored yet. Never creates an account. Returns the stored
    /// account (null when there is none here and none at the provider).
    /// </summary>
    public async Task<SellerPaymentAccount?> ReconcileAsync(string seller, SellerPaymentAccount? current, CancellationToken ct)
    {
        if (current?.Status == SellerPaymentStatus.Active || !provider.IsConfigured) return current;
        IReadOnlyList<SellerAccountSnapshot> found;
        try
        {
            found = await provider.FindSellerAccountsAsync(seller, ct);
        }
        catch (PaymentProviderException e) when (current is not null)
        {
            // Keep what is stored; without a stored account the caller must not create a possible duplicate.
            logger.LogWarning("Couldn't look up {Seller}'s accounts at the provider: {Message}", seller, e.Message);
            return current;
        }
        var best = Best(found);
        if (best is null) return current;
        if (current is not null && (current.ProviderAccountId == best.AccountId || Rank(StatusOf(best)) >= Rank(current.Status))) return current;
        return (await LinkAsync(seller, current, best, ct)).Account;
    }

    /// <summary>
    /// For the CURRENT database: every seller's account at the provider (tagged app + seller) is linked in
    /// SellerPaymentAccounts – the best one when a seller has several – and its status is read again. Nothing is created
    /// at the provider. Admin endpoint / sync-seller-payments.sh.
    /// </summary>
    public async Task<List<SellerPaymentSyncResult>> SyncAllAsync(CancellationToken ct)
    {
        var atProvider = (await provider.FindSellerAccountsAsync(null, ct)).ToLookup(a => a.Seller!);
        var stored = await db.SellerPaymentAccounts.ToListAsync(ct);
        var results = new List<SellerPaymentSyncResult>();
        foreach (var seller in atProvider.Select(g => g.Key).Union(stored.Select(s => s.Seller)).Order(StringComparer.Ordinal))
        {
            var current = stored.FirstOrDefault(s => s.Seller == seller);
            var candidates = atProvider[seller].ToList();
            // A stored account without the tags (older ones) still counts.
            if (current is not null && candidates.All(c => c.AccountId != current.ProviderAccountId))
            {
                try
                {
                    candidates.Add(await provider.GetSellerAccountAsync(current.ProviderAccountId, ct));
                }
                catch (PaymentProviderException e)
                {
                    logger.LogWarning("Stored payment account {AccountId} of {Seller} can't be read: {Message}", current.ProviderAccountId, seller, e.Message);
                }
            }
            if (Best(candidates) is not { } best)
            {
                results.Add(new SellerPaymentSyncResult(seller, current?.ProviderAccountId, current?.Status.ToString() ?? "None", "not found at Stripe – left as is", 0));
                continue;
            }
            var (account, action) = await LinkAsync(seller, current, best, ct);
            results.Add(new SellerPaymentSyncResult(seller, account.ProviderAccountId, account.Status.ToString(), action, candidates.Count));
        }
        return results;
    }

    /// <summary>Stores <paramref name="best"/> as the seller's account (new row, or replacing the stored account id).</summary>
    private async Task<(SellerPaymentAccount Account, string Action)> LinkAsync(string seller, SellerPaymentAccount? current, SellerAccountSnapshot best,
        CancellationToken ct)
    {
        string action;
        if (current is null)
        {
            current = new SellerPaymentAccount
            {
                Seller = seller, Provider = provider.Name, ProviderAccountId = best.AccountId,
                Country = best.Country.Length == 2 ? best.Country : "US",
            };
            db.SellerPaymentAccounts.Add(current);
            action = "linked";
        }
        else if (current.ProviderAccountId != best.AccountId)
        {
            action = $"relinked (was {current.ProviderAccountId})";
            current.ProviderAccountId = best.AccountId;
            if (best.Country.Length == 2) current.Country = best.Country;
        }
        else action = "refreshed";
        Apply(current, best);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Payment account {AccountId} of {Seller}: {Action} ({Status})", best.AccountId, seller, action, current.Status);
        return (current, action);
    }

    /// <summary>A seller's best account: Active first, then the furthest in setup, then the newest.</summary>
    public static SellerAccountSnapshot? Best(IEnumerable<SellerAccountSnapshot> accounts) =>
        accounts.OrderBy(a => Rank(StatusOf(a))).ThenByDescending(a => a.DetailsSubmitted).ThenByDescending(a => a.Created ?? DateTime.MinValue)
            .FirstOrDefault();

    private static int Rank(SellerPaymentStatus status) => status switch
    {
        SellerPaymentStatus.Active => 0,
        SellerPaymentStatus.Pending => 1,
        SellerPaymentStatus.ActionNeeded => 2,
        _ => 3,
    };

    /// <summary>account.updated: the stored status follows the provider. Unknown accounts are ignored.</summary>
    public async Task<bool> SyncAsync(SellerAccountSnapshot snapshot, CancellationToken ct)
    {
        var account = await db.SellerPaymentAccounts.FirstOrDefaultAsync(a => a.ProviderAccountId == snapshot.AccountId, ct);
        if (account is null) return false;
        Apply(account, snapshot);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Payments:AllowInactiveSellersInTestMode – only with TEST keys (or the test provider); never with live keys.
    /// </summary>
    public bool TestOverride => _opt.AllowInactiveSellersInTestMode && provider.IsTestMode;

    /// <summary>Can buyers pay this seller now? Active – or the test-only override.</summary>
    public async Task<bool> CanReceivePaymentsAsync(string seller, CancellationToken ct) =>
        TestOverride || (await FindAsync(seller, ct))?.Status == SellerPaymentStatus.Active;

    /// <summary>Can this seller publish a listing in this country? (Null = yes.)</summary>
    public async Task<string?> PublishBlockedReasonAsync(string seller, string? listingCountry, CancellationToken ct)
    {
        if (!IsOnline(listingCountry)) return null;
        return await CanReceivePaymentsAsync(seller, ct)
            ? null
            : "Finish your payment setup first (Profile → Payments) – buyers pay online, and you get paid through it.";
    }

    public static void Apply(SellerPaymentAccount account, SellerAccountSnapshot s)
    {
        account.ChargesEnabled = s.ChargesEnabled;
        account.PayoutsEnabled = s.PayoutsEnabled;
        account.TransfersActive = s.TransfersActive;
        account.CardPaymentsRequested = s.CardPaymentsRequested;
        account.DetailsSubmitted = s.DetailsSubmitted;
        account.RequirementsDue = s.RequirementsDue.Count == 0 ? null : Truncate(string.Join(",", s.RequirementsDue), 2000);
        account.DisabledReason = s.DisabledReason is null ? null : Truncate(s.DisabledReason, 200);
        account.Status = StatusOf(s);
        account.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Active: payouts on and the transfers capability active (and card payments, when requested – US sellers).
    /// Not started: onboarding not submitted. Action needed: the provider asks for more or disabled the account.
    /// Pending: submitted, being verified.
    /// </summary>
    public static SellerPaymentStatus StatusOf(SellerAccountSnapshot s)
    {
        if (s.PayoutsEnabled && s.TransfersActive && (!s.CardPaymentsRequested || s.ChargesEnabled)) return SellerPaymentStatus.Active;
        if (!s.DetailsSubmitted) return SellerPaymentStatus.NotStarted;
        if (s.RequirementsDue.Count > 0 || !string.IsNullOrEmpty(s.DisabledReason) && !s.DisabledReason.StartsWith("requirements.pending_verification"))
            return SellerPaymentStatus.ActionNeeded;
        return SellerPaymentStatus.Pending;
    }

    private static string Truncate(string text, int max) => text.Length > max ? text[..max] : text;
}

/// <summary>One seller after the sync: the linked account, its status, what was done, and how many accounts Stripe has.</summary>
public record SellerPaymentSyncResult(string Seller, string? AccountId, string Status, string Action, int AccountsAtProvider);

/// <summary>
/// The amounts of a payment, in the currency's smallest unit (cents). The app keeps Payments:CommissionPercent of the
/// ITEM price; shipping goes fully to the seller; the provider's fee is paid by the platform (not taken from the seller).
/// </summary>
public static class PaymentAmounts
{
    /// <summary>Currencies with two decimals (USD, EUR). Prices in the app are whole units, so × 100.</summary>
    public static long ToMinor(int wholeUnits) => wholeUnits * 100L;

    public static long Commission(long itemMinor, decimal percent) =>
        (long)Math.Round(itemMinor * percent / 100m, 0, MidpointRounding.AwayFromZero);

    public record Breakdown(long Item, long Shipping, long Total, long Commission, long SellerAmount);

    /// <summary>Item $340 + shipping $20 = $360; 1 % fee $3.40; the seller receives $356.60.</summary>
    public static Breakdown For(long itemMinor, long shippingMinor, decimal commissionPercent)
    {
        var commission = Commission(itemMinor, commissionPercent);
        return new Breakdown(itemMinor, shippingMinor, itemMinor + shippingMinor, commission, itemMinor + shippingMinor - commission);
    }
}
