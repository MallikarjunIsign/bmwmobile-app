using System.Net;
using System.Text;
using AuctionService.Errors;
using AuctionService.Payments;
using Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AuctionService.Controllers;

/// <summary>What a seller sees in Profile → Payments.</summary>
public record SellerPaymentAccountDto(
    // Online payment is available in the seller's country (US, IE). False: "coming soon in your country".
    bool Available,
    string? Country,
    // NotAvailable, NotStarted, Pending, Active, ActionNeeded
    string Status,
    string Message,
    bool CanPublish,
    int RequirementsDue);

public record PaymentLinkDto(string Url);

/// <summary>"The winner didn't pay: it's yours for your bid of $340 – accept within 47 h".</summary>
public record SecondChanceOfferDto(string Id, string ListingId, string Title, int Amount, string? Currency, DateTime ExpiresAt, int Round);

/// <summary>A reason / note (report a problem, cancel and refund, hold).</summary>
public record ProblemRequest([System.ComponentModel.DataAnnotations.MaxLength(250)] string? Text);

/// <summary>Amount in cents (null = everything left) and why.</summary>
public record AdminRefundRequest(long? Amount, [System.ComponentModel.DataAnnotations.MaxLength(250)] string? Reason);

public record AdminReleaseRequest(bool Complete);

/// <summary>What the app needs to show payments (the publishable key is public by design).</summary>
public record PaymentConfigDto(bool Enabled, string? PublishableKey, IReadOnlyList<string> Countries, decimal CommissionPercent);

/// <summary>
/// Online payments (Stripe Connect, test mode): sellers' payout accounts, buyer payments, receipts, refunds, payouts,
/// the admin tools and the provider's webhooks.
/// </summary>
[ApiController]
[Authorize]
[Route("api/payments")]
public class PaymentsController(SellerPaymentService sellers, IPaymentProvider provider, PaymentWebhookService webhooks,
    IOptions<PaymentOptions> options, IOptions<StripeOptions> stripe, TimeProvider clock, ILogger<PaymentsController> logger) : ControllerBase
{
    private readonly PaymentOptions _opt = options.Value;
    private string Username => User.Identity?.Name ?? throw new UnauthorizedAccessException();

    /// <summary>The account's country (admin-set), else the chosen location's country.</summary>
    private string? MyCountry =>
        User.FindFirst(AppRoles.AccountCountryClaim)?.Value ?? User.FindFirst(PartRequestAudience.CountryClaim)?.Value;

    [HttpGet("config")]
    public PaymentConfigDto Config() => new(provider.IsConfigured,
        provider.IsConfigured ? stripe.Value.PublishableKey : null, _opt.Countries, _opt.CommissionPercent);

    /// <summary>
    /// The seller's payment status (read again from the provider when an account exists). Not active yet: an account the
    /// seller finished while another database was in use is linked instead (never created here).
    /// </summary>
    [SellerOnly]
    [HttpGet("seller-account")]
    public async Task<ActionResult<SellerPaymentAccountDto>> SellerAccount(CancellationToken ct)
    {
        var account = await sellers.FindAsync(Username, ct);
        try
        {
            if (account is not null) account = await sellers.RefreshAsync(account, ct);
            if (account is not null || sellers.IsOnline(MyCountry)) account = await sellers.ReconcileAsync(Username, account, ct);
        }
        catch (PaymentProviderException e)
        {
            // Show the stored status; the webhook keeps it current anyway.
            logger.LogWarning("Couldn't refresh payment account of {Seller}: {Message}", Username, e.Message);
        }
        return ToDto(account, MyCountry);
    }

    /// <summary>First visit: creates the seller's payment account (US / IE) if it doesn't exist yet.</summary>
    [SellerOnly]
    [HttpPost("seller-account")]
    public async Task<ActionResult<SellerPaymentAccountDto>> EnsureSellerAccount(CancellationToken ct)
    {
        try
        {
            return ToDto(await sellers.EnsureAsync(Username, MyCountry, Email, ListingsController.DisplayName(User), ct), MyCountry);
        }
        catch (PaymentProviderException e)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = e.Message, code = "PAYMENT_PROVIDER_ERROR" });
        }
    }

    /// <summary>The provider-hosted onboarding (opened in the browser; returns to the app).</summary>
    [SellerOnly]
    [HttpPost("seller-account/onboarding-link")]
    public async Task<ActionResult<PaymentLinkDto>> OnboardingLink(CancellationToken ct)
    {
        try
        {
            var account = await sellers.EnsureAsync(Username, MyCountry, Email, ListingsController.DisplayName(User), ct);
            if (account is null) return NotAvailable();
            var baseUrl = _opt.PublicBaseUrl.TrimEnd('/');
            return new PaymentLinkDto(await provider.CreateOnboardingLinkAsync(account.ProviderAccountId,
                $"{baseUrl}/payments/stripe/onboarding-return", $"{baseUrl}/payments/stripe/onboarding-refresh", ct));
        }
        catch (PaymentProviderException e)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = e.Message, code = "PAYMENT_PROVIDER_ERROR" });
        }
    }

    /// <summary>The provider-hosted dashboard (payouts, bank account). Only after onboarding was submitted.</summary>
    [SellerOnly]
    [HttpPost("seller-account/dashboard-link")]
    public async Task<ActionResult<PaymentLinkDto>> DashboardLink(CancellationToken ct)
    {
        var account = await sellers.FindAsync(Username, ct);
        if (account is null) return NotAvailable();
        if (!account.DetailsSubmitted)
            return BadRequest(new { message = "Finish the payment setup first.", code = "ONBOARDING_NOT_FINISHED" });
        try
        {
            return new PaymentLinkDto(await provider.CreateDashboardLinkAsync(account.ProviderAccountId, ct));
        }
        catch (PaymentProviderException e)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = e.Message, code = "PAYMENT_PROVIDER_ERROR" });
        }
    }

    // ── buyers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Buy now (USA / Ireland): reserves the quantity for Payments:ReservationMinutes and opens a payment for item ×
    /// quantity + the shipping charge (calculated here). The order is created only when Stripe confirms the payment.
    /// </summary>
    [HttpPost("checkout/buy-now")]
    public Task<ActionResult<CheckoutSessionDto>> BuyNow(BuyNowRequest request, [FromServices] CheckoutService checkout, CancellationToken ct) =>
        Run(() =>
        {
            var (email, phone) = ListingsController.Contact(User);
            return checkout.StartBuyNowAsync(Username, email, phone, request, ct);
        });

    /// <summary>Pay for an order waiting for payment (auction win / accepted second-chance offer).</summary>
    [HttpPost("orders/{orderId}/pay")]
    public Task<ActionResult<CheckoutSessionDto>> PayOrder(string orderId, [FromServices] CheckoutService checkout, CancellationToken ct) =>
        Run(() => checkout.StartOrderPaymentAsync(Username, Email, orderId, ct));

    /// <summary>The payment sheet was closed or failed: the reservation is released at once.</summary>
    [HttpPost("{paymentId}/cancel")]
    public async Task<IActionResult> Cancel(string paymentId, [FromServices] CheckoutService checkout, CancellationToken ct)
    {
        try
        {
            await checkout.CancelAsync(Username, paymentId, ct);
            return NoContent();
        }
        catch (CheckoutException e)
        {
            return StatusCode(e.Status, new { message = e.Message, code = e.Code });
        }
    }

    /// <summary>After the sheet: Paid once the webhook arrived (the app polls this), with the order.</summary>
    [HttpGet("{paymentId}")]
    public Task<ActionResult<PaymentStatusDto>> Status(string paymentId, [FromServices] CheckoutService checkout, CancellationToken ct) =>
        Run(() => checkout.StatusAsync(Username, paymentId, ct));

    /// <summary>Second-chance offers for the caller (the winner didn't pay; it's offered at their own highest bid).</summary>
    [HttpGet("offers")]
    public async Task<List<SecondChanceOfferDto>> Offers([FromServices] Data.AuctionDbContext db, CancellationToken ct)
    {
        var mine = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.SecondChanceOffers.Where(o => o.Bidder == Username && o.Status == SecondChanceStatus.Offered), ct);
        var ids = mine.Select(o => o.AuctionId).ToList();
        var items = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToDictionaryAsync(
            db.Auctions.Where(a => ids.Contains(a.Id)).Select(a => new { a.Id, Title = a.Item.Title ?? a.Item.Make, a.Item.Currency }), a => a.Id, ct);
        return mine.OrderBy(o => o.ExpiresAt).Select(o => new SecondChanceOfferDto(o.Id, o.AuctionId, items.GetValueOrDefault(o.AuctionId)?.Title ?? "",
            o.Amount, items.GetValueOrDefault(o.AuctionId)?.Currency, o.ExpiresAt, o.Round)).ToList();
    }

    [HttpPost("offers/{offerId}/accept")]
    public Task<ActionResult<OrderDto>> AcceptOffer(string offerId, [FromServices] PaymentDeadlineService deadlines, CancellationToken ct) =>
        Run(async () =>
        {
            var (email, phone) = ListingsController.Contact(User);
            return OrdersController.ToDto(await deadlines.AcceptOfferAsync(Username, offerId, email, phone, ct), Username, now: clock.GetUtcNow().UtcDateTime);
        });

    [HttpPost("offers/{offerId}/decline")]
    public async Task<IActionResult> DeclineOffer(string offerId, [FromServices] PaymentDeadlineService deadlines, CancellationToken ct)
    {
        try
        {
            await deadlines.DeclineOfferAsync(Username, offerId, ct);
            return NoContent();
        }
        catch (CheckoutException e)
        {
            return StatusCode(e.Status, new { message = e.Message, code = e.Code });
        }
    }

    /// <summary>The caller's non-payment strikes and whether they may bid.</summary>
    [HttpGet("strikes/me")]
    public async Task<object> MyStrikes([FromServices] StrikeService strikes, CancellationToken ct) => new
    {
        strikes = await strikes.ActiveStrikesAsync(Username, ct),
        limit = _opt.StrikeLimit,
        blockedReason = await strikes.BiddingBlockedReasonAsync(Username, ct),
    };

    /// <summary>Admin: clear a user's non-payment strikes (they may bid again).</summary>
    [HttpPost("admin/strikes/{user}/clear")]
    public async Task<IActionResult> ClearStrikes(string user, [FromServices] StrikeService strikes, CancellationToken ct)
    {
        if (!AppRoles.IsAdmin(User)) return Forbidden.With(AppRoles.AdminOnlyMessage, AppRoles.AdminOnlyCode);
        return Ok(new { cleared = await strikes.ClearAsync(user, Username, ct) });
    }

    // ── after the payment (Step 3): receipts, problems, refunds, payouts ─

    /// <summary>Buyer: the receipt (what was paid, card, refunds, Stripe's receipt page). Seller: the payout statement.</summary>
    [HttpGet("orders/{orderId}/receipt")]
    public Task<ActionResult<ReceiptDto>> Receipt(string orderId, [FromServices] SellerPayoutService payouts, CancellationToken ct) =>
        Run(() => payouts.ReceiptAsync(Username, orderId, ct));

    /// <summary>Buyer: something is wrong with the order – it goes on hold (no auto-complete, no payout) until sorted out.</summary>
    [HttpPost("orders/{orderId}/problem")]
    public Task<ActionResult<OrderDto>> ReportProblem(string orderId, ProblemRequest request, [FromServices] SellerPayoutService payouts, CancellationToken ct) =>
        Run(async () => await OrderViewAsync(await payouts.ReportProblemAsync(Username, orderId, request.Text, ct), payouts, ct));

    /// <summary>Seller: can't fulfil a paid order (before pickup / shipping) – it is cancelled and refunded in full.</summary>
    [SellerOnly]
    [HttpPost("orders/{orderId}/cancel-and-refund")]
    public Task<ActionResult<OrderDto>> CancelAndRefund(string orderId, ProblemRequest request, [FromServices] SellerPayoutService payouts, CancellationToken ct) =>
        Run(async () => await OrderViewAsync(await payouts.SellerCancelAsync(Username, orderId, request.Text, ct), payouts, ct));

    // ── admin ────────────────────────────────────────────────────────────

    /// <summary>
    /// Admin: links every seller's existing Stripe account (app=shopcarparts, seller=…) in THIS database and reads its
    /// status again. Creates nothing at Stripe. Used after switching between the local and the cloud database.
    /// </summary>
    [HttpPost("admin/seller-accounts/sync")]
    public Task<ActionResult<List<SellerPaymentSyncResult>>> AdminSyncSellerAccounts(CancellationToken ct) =>
        Admin(() => sellers.SyncAllAsync(ct));

    /// <summary>Admin: payments, newest first (filter by status – Succeeded, PartiallyRefunded, Refunded, Disputed, Failed – or user).</summary>
    [HttpGet("admin/payments")]
    public async Task<ActionResult<List<AdminPaymentDto>>> AdminPayments([FromQuery] string? status, [FromQuery] string? user,
        [FromServices] SellerPayoutService payouts, CancellationToken ct, [FromQuery] int take = 50)
    {
        if (!AppRoles.IsAdmin(User)) return Forbidden.With(AppRoles.AdminOnlyMessage, AppRoles.AdminOnlyCode);
        return await payouts.AdminListAsync(status, user, take, ct);
    }

    /// <summary>Admin: one order's payment, refunds, payout and dispute (with the Stripe ids for the Dashboard).</summary>
    [HttpGet("admin/orders/{orderId}")]
    public Task<ActionResult<AdminOrderPaymentDto>> AdminOrder(string orderId, [FromServices] SellerPayoutService payouts, CancellationToken ct) =>
        Admin(() => payouts.AdminDetailAsync(orderId, ct));

    /// <summary>Admin: refund all (no amount) or part (amount in cents) of an order to the buyer's card.</summary>
    [HttpPost("admin/orders/{orderId}/refund")]
    public Task<ActionResult<OrderDto>> AdminRefund(string orderId, AdminRefundRequest request, [FromServices] SellerPayoutService payouts, CancellationToken ct) =>
        Admin(async () => await OrderViewAsync(await payouts.AdminRefundAsync(Username, orderId, request.Amount, request.Reason, ct), payouts, ct));

    /// <summary>Admin: put an order on hold (no auto-complete, no payout).</summary>
    [HttpPost("admin/orders/{orderId}/hold")]
    public Task<ActionResult<OrderDto>> AdminHold(string orderId, ProblemRequest request, [FromServices] SellerPayoutService payouts, CancellationToken ct) =>
        Admin(async () => await OrderViewAsync(await payouts.AdminHoldAsync(Username, orderId, request.Text, ct), payouts, ct));

    /// <summary>Admin: release a hold; complete = true also completes the order now and pays the seller.</summary>
    [HttpPost("admin/orders/{orderId}/release")]
    public Task<ActionResult<OrderDto>> AdminRelease(string orderId, AdminReleaseRequest request, [FromServices] SellerPayoutService payouts, CancellationToken ct) =>
        Admin(async () => await OrderViewAsync(await payouts.AdminReleaseAsync(Username, orderId, request.Complete, ct), payouts, ct));

    /// <summary>Admin: send a waiting payout again now.</summary>
    [HttpPost("admin/transfers/{transferId}/retry")]
    public Task<ActionResult<object>> AdminRetryTransfer(string transferId, [FromServices] SellerPayoutService payouts, CancellationToken ct) =>
        Admin(async () => (object)TransferView(await payouts.AdminRetryAsync(transferId, ct)));

    /// <summary>Admin: take (part of) a sent payout back from the seller (amount in cents of the transfer's currency).</summary>
    [HttpPost("admin/transfers/{transferId}/reverse")]
    public Task<ActionResult<object>> AdminReverseTransfer(string transferId, AdminRefundRequest request, [FromServices] SellerPayoutService payouts, CancellationToken ct) =>
        Admin(async () => (object)TransferView(await payouts.AdminReverseAsync(Username, transferId, request.Amount, request.Reason, ct)));

    private static object TransferView(PaymentTransfer t) => new
    {
        t.Id, t.OrderId, status = t.Status.ToString(), t.Amount, t.Currency, t.ReversedAmount, t.SellerAmount, t.SellerCurrency,
        t.ArrivedAmount, t.ArrivedCurrency, t.FailureMessage, t.TransferredAt,
    };

    private async Task<OrderDto> OrderViewAsync(Entities.Order order, SellerPayoutService payouts, CancellationToken ct)
    {
        var info = await payouts.InfoForAsync([order], Username, ct);
        return OrdersController.ToDto(order, Username, info.GetValueOrDefault(order.Id), clock.GetUtcNow().UtcDateTime);
    }

    private async Task<ActionResult<T>> Admin<T>(Func<Task<T>> action) =>
        AppRoles.IsAdmin(User) ? await Run(action) : Forbidden.With(AppRoles.AdminOnlyMessage, AppRoles.AdminOnlyCode);

    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (CheckoutException e)
        {
            return StatusCode(e.Status, new { message = e.Message, code = e.Code });
        }
        catch (PaymentProviderException e)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = e.Message, code = "PAYMENT_PROVIDER_ERROR" });
        }
    }

    /// <summary>Stripe's onboarding returns here (in the phone's browser): back to the app.</summary>
    [AllowAnonymous]
    [HttpGet("stripe/onboarding-return")]
    public ContentResult OnboardingReturn() => Page("Payment setup saved",
        "Your details were sent to Stripe. Go back to ShopCarParts – Profile → Payments shows your status.");

    /// <summary>The onboarding link expired or was used: start again from the app (no new link without sign-in).</summary>
    [AllowAnonymous]
    [HttpGet("stripe/onboarding-refresh")]
    public ContentResult OnboardingRefresh() => Page("This link has expired",
        "Open ShopCarParts → Profile → Payments and tap \"Continue setup\" to get a new link.");

    /// <summary>
    /// Stripe webhooks (no sign-in): the signature is checked with Stripe:WebhookSecret; each event is handled once;
    /// events about other projects' objects (no app=shopcarparts tag) are ignored.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("stripe/webhook")]
    public async Task<IActionResult> StripeWebhook(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync(ct);
        PaymentWebhookEvent evt;
        try
        {
            evt = provider.ParseWebhook(payload, Request.Headers["Stripe-Signature"]);
        }
        catch (WebhookSignatureException e)
        {
            logger.LogWarning("Rejected a Stripe webhook: {Reason}", e.Message);
            return BadRequest(new { message = "Invalid signature" });
        }
        return Ok(new { received = true, outcome = await webhooks.HandleAsync(evt, ct) });
    }

    private string? Email => ListingsController.Contact(User).Email;

    private ObjectResult NotAvailable() => BadRequest(new
    {
        message = "Online payment is coming soon in your country.",
        code = "PAYMENTS_NOT_AVAILABLE",
    });

    private SellerPaymentAccountDto ToDto(SellerPaymentAccount? account, string? country)
    {
        if (account is null)
        {
            // Payments switched off on this server (no Stripe keys): selling works as before.
            if (!provider.IsConfigured)
                return new SellerPaymentAccountDto(false, country, "NotAvailable", "Online payment isn't switched on yet.", true, 0);
            return sellers.IsOnline(country)
                ? new SellerPaymentAccountDto(true, country, "NotStarted", "Set up payments to sell: buyers pay online and you get paid to your bank.", false, 0)
                : new SellerPaymentAccountDto(false, country, "NotAvailable", "Online payment is coming soon in your country.", true, 0);
        }
        var (status, message) = account.Status switch
        {
            SellerPaymentStatus.Active => ("Active", "Payments are active: buyers can pay you online."),
            SellerPaymentStatus.Pending => ("Pending", "Stripe is verifying your details. This usually takes a few minutes."),
            SellerPaymentStatus.ActionNeeded => ("ActionNeeded", "Stripe needs more information. Tap \"Continue setup\"."),
            _ => ("NotStarted", "Set up payments to sell: buyers pay online and you get paid to your bank."),
        };
        var dueCount = string.IsNullOrEmpty(account.RequirementsDue) ? 0 : account.RequirementsDue.Split(',').Length;
        return new SellerPaymentAccountDto(true, account.Country, status, message, account.Status == SellerPaymentStatus.Active, dueCount);
    }

    private ContentResult Page(string title, string text)
    {
        var deepLink = WebUtility.HtmlEncode(_opt.AppReturnUrl);
        var html = $$"""
            <!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{WebUtility.HtmlEncode(title)}}</title>
            <style>body{font-family:sans-serif;background:#07071C;color:#fff;padding:32px;text-align:center}
            a{display:inline-block;margin-top:24px;padding:14px 28px;background:#5B2EFF;color:#fff;border-radius:10px;text-decoration:none;font-weight:700}</style>
            </head><body><h2>{{WebUtility.HtmlEncode(title)}}</h2><p>{{WebUtility.HtmlEncode(text)}}</p>
            <a href="{{deepLink}}">Back to ShopCarParts</a>
            <script>setTimeout(function(){location.href="{{deepLink}}";},800);</script></body></html>
            """;
        return Content(html, "text/html", Encoding.UTF8);
    }
}
