namespace AuctionService.Payments;

/// <summary>
/// Online payment rules ("Payments" in appsettings). Every number the business may change is here, not in code.
/// </summary>
public class PaymentOptions
{
    public const string SectionName = "Payments";

    /// <summary>The app's commission on the ITEM price (shipping goes fully to the seller), in percent.</summary>
    public decimal CommissionPercent { get; set; } = 1.0m;

    /// <summary>An auction winner pays within this many hours of the end.</summary>
    public int PaymentDeadlineHours { get; set; } = 48;

    /// <summary>How many next-highest bidders get a second-chance offer when the winner doesn't pay.</summary>
    public int SecondChanceOffers { get; set; } = 3;

    /// <summary>Buy now: the quantity is held for the buyer this long while they pay.</summary>
    public int ReservationMinutes { get; set; } = 15;

    /// <summary>An order is completed (and the seller paid) this many days after Delivered / Picked up.</summary>
    public int AutoCompleteDays { get; set; } = 7;

    /// <summary>Non-payment strikes within <see cref="StrikeWindowMonths"/> that block bidding.</summary>
    public int StrikeLimit { get; set; } = 2;

    public int StrikeWindowMonths { get; set; } = 12;

    /// <summary>Countries with online payment now (US, IE). Others keep the current flow: "coming soon".</summary>
    public string[] Countries { get; set; } = ["US", "IE"];

    /// <summary>
    /// Every provider object we create is tagged with metadata app=&lt;AppTag&gt;; webhook events without it are
    /// ignored (the Stripe test account is shared with another project).
    /// </summary>
    public string AppTag { get; set; } = "shopcarparts";

    /// <summary>
    /// Where the phone's browser reaches the Gateway (Stripe's hosted onboarding returns there, then to the app).
    /// The Android emulator reaches the PC as 10.0.2.2.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "http://10.0.2.2:6001";

    /// <summary>The app's deep link, opened after the hosted onboarding.</summary>
    public string AppReturnUrl { get; set; } = "shopcarparts://payments";

    /// <summary>
    /// Demos / tests: the payment deadline in MINUTES instead of <see cref="PaymentDeadlineHours"/> (e.g. 3). Null = off.
    /// </summary>
    public int? PaymentDeadlineMinutes { get; set; }

    /// <summary>How often the payment jobs run (reservations, reminders, deadlines, second-chance offers).</summary>
    public int JobIntervalSeconds { get; set; } = 30;

    /// <summary>Run those jobs in this process (tests run them by hand).</summary>
    public bool RunJobs { get; set; } = true;

    /// <summary>
    /// TESTING ONLY, off by default: sellers whose Stripe account isn't Active yet may still publish and be paid
    /// (Buy now / auctions). Works only with Stripe TEST keys (or the test provider) – ignored with live keys.
    /// </summary>
    public bool AllowInactiveSellersInTestMode { get; set; }

    /// <summary>BidService, for the bid history (second-chance offers go to the next-highest bidders).</summary>
    public string BidServiceUrl { get; set; } = "http://localhost:7003";

    /// <summary>Demos / tests: auto-complete this many MINUTES after the handover instead of <see cref="AutoCompleteDays"/>.</summary>
    public int? AutoCompleteMinutes { get; set; }

    /// <summary>
    /// The platform's balance currency (the US Stripe account settles in USD): seller transfers are made in it, and a
    /// EUR payment's share is converted at the charge's rate (Irish sellers see "about €…").
    /// </summary>
    public string PlatformCurrency { get; set; } = "USD";

    public TimeSpan AutoCompleteWindow => AutoCompleteMinutes is > 0
        ? TimeSpan.FromMinutes(AutoCompleteMinutes.Value)
        : TimeSpan.FromDays(Math.Max(AutoCompleteDays, 1));

    public TimeSpan PaymentWindow => PaymentDeadlineMinutes is > 0
        ? TimeSpan.FromMinutes(PaymentDeadlineMinutes.Value)
        : TimeSpan.FromHours(Math.Max(PaymentDeadlineHours, 1));

    public bool IsOnlineCountry(string? country) =>
        !string.IsNullOrWhiteSpace(country) && Countries.Contains(country.Trim(), StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Stripe keys. ONLY from user-secrets / environment variables – never in appsettings, logs or the repo.
/// Test mode only for now (sk_test_ / pk_test_).
/// </summary>
public class StripeOptions
{
    public const string SectionName = "Stripe";

    public string? SecretKey { get; set; }
    public string? PublishableKey { get; set; }
    public string? WebhookSecret { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);

    /// <summary>Live keys are refused while the integration is test-only.</summary>
    public bool IsTestMode => SecretKey?.StartsWith("sk_test_", StringComparison.Ordinal) == true ||
                              SecretKey?.StartsWith("rk_test_", StringComparison.Ordinal) == true;
}
