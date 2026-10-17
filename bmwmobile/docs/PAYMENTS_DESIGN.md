# Online payments (Stripe, test mode)

Status (16 Oct 2026): **Steps 1–3 built and live-tested** in the Shopcarparts Stripe sandbox (US, test mode, Connect
enabled as a marketplace):

- **Step 1:** provider layer, seller payment accounts (Accounts v2) with Stripe-hosted onboarding, payment tables, webhooks,
  Stripe CLI.
- **Step 2:** buyer payment with the Payment Sheet, Buy now reservation, auction payment deadline with reminders,
  second-chance offers, strikes.
- **Step 3:** the seller's payout on completion (or automatically after 7 days), "about €…" for Irish sellers, refunds,
  holds and disputes, admin endpoints, receipts.

`Payments:AllowInactiveSellersInTestMode` (off by default, ignored with live keys) was used for Step 2 before Connect was on.
With Active sellers it isn't needed.

## 1. Decisions

| Topic | Decision |
|---|---|
| Countries | USA (USD) and Ireland (EUR) with Stripe. India / Nigeria later (Razorpay, Paystack): "Online payment coming soon in your country", current flow unchanged |
| Provider | Behind `IPaymentProvider` (AuctionService `Payments/`); `StripePaymentProvider` is the only implementation. No keys → `NotConfiguredPaymentProvider`: payments off, current flow |
| Stripe account | The **Shopcarparts** sandbox (US platform, **test mode only**: `sk_test_`/`pk_test_`; live keys refused), used only by this app. Every object still gets metadata `app=shopcarparts`; webhook events without it (e.g. Stripe's sample objects from `stripe trigger`) are ignored; no account-wide settings changed by the app |
| Keys | `Stripe:SecretKey`, `Stripe:PublishableKey`, `Stripe:WebhookSecret` – user-secrets / env vars only, never logged |
| Sellers | Stripe Connect connected accounts created with **Accounts v2** (`POST /v2/core/accounts`) – new Stripe platforms can't create v1 accounts. Express dashboard, Stripe-hosted onboarding (v1 account links work with v2 accounts) |
| Charges | **Separate charges and transfers**: the buyer pays the platform; `transfer_group=order_<orderId>`; the seller's share is transferred when the order is completed |
| Commission | `Payments:CommissionPercent` (1 %) of the **item price**; shipping goes fully to the seller; Stripe's fees are paid by the platform |
| No cash on delivery | Every order in USA / Ireland is paid online before fulfilment |
| Payout | When the buyer confirms receipt (Completed), or automatically `Payments:AutoCompleteDays` (7) after the seller marks it **Picked up / Shipped** – unless the order is on hold |
| Irish sellers | The platform balance is USD: the EUR share is sent in USD at the charge's own rate and Stripe converts it back – the seller is shown "about €…" before, and what actually arrived after |
| Auctions | Pay within `Payments:PaymentDeadlineHours` (48); then up to `Payments:SecondChanceOffers` (3) next-highest bidders; `Payments:StrikeLimit` (2) non-payment strikes in `StrikeWindowMonths` (12) block bidding |
| Buy now | Quantity reserved `Payments:ReservationMinutes` (15) while the buyer pays |
| Money | Smallest unit (cents) as `long`, always with the currency; never floating point |

## 2. Why AuctionService owns payments

Orders, listings / stock, shipping charges and fulfilment already live in AuctionService, and a payment changes them in the
same database transaction (order Paid + quantity, reservation released + order cancelled, refund + order state). A separate
payment service would need copies of orders and stock and could drift. IdentityService only tells AuctionService about new
sellers (internal HTTP, best effort), as it already does for roles.

## 3. Ireland: cross-border (checked in Stripe's docs and live)

- A US platform can transfer to connected accounts in the EEA: Stripe supports cross-border transfers between the US,
  Canada, the UK, the EEA and Switzerland, with separate charges and transfers **without `on_behalf_of`**
  (docs.stripe.com/connect/account-capabilities → Cross-border transfers). Tested live: a USD transfer to the Irish account
  arrives in EUR.
- Accounts v2 needs the merchant `card_payments` capability next to the recipient `stripe_transfers`, for US **and** Irish
  sellers. "Active" = payouts enabled + transfers active + charges enabled.
- **Currency (decided):** a EUR charge settles on the US platform in USD (e.g. €95.00 → $106.51 at 1.12119). A transfer funded
  by the charge (`source_transaction`) must be in that balance currency, so the Irish seller's share is sent in **USD at the
  charge's rate** and Stripe converts it to EUR. Before the payout the seller sees **"about €94.05"**; after it, what arrived
  ("Paid out €94.04 (sent as $105.44)"), read from the seller's balance transaction.

## 4. Seller payment accounts (Step 1)

- Created: when an admin creates a seller (`POST /api/admin/sellers`) or upgrades a buyer (`make-seller`) – IdentityService calls
  AuctionService `POST /api/internal/payment-accounts` – or on the seller's first visit to Profile → Payments
  (`POST /payments/seller-account`). Country = the account's country (token claim `account_country`, else the location's).
- Accounts v2 request: `identity.country`, `entity_type: individual`, `dashboard: express`,
  `defaults.responsibilities` fees and losses = `application` (required with Express; the platform charges the buyer anyway),
  `defaults.currency` usd / eur, configuration `merchant.card_payments` + `recipient.stripe_balance.stripe_transfers`,
  `metadata.app`. Prefilled from what the app knows: contact e-mail, display name, product description
  ("Sells used and new car parts on the ShopCarParts marketplace"), MCC 5533 (automotive parts). Stripe's hosted onboarding
  asks the seller for the rest (identity, bank, terms – only the seller can accept Stripe's terms).
- Status: **NotStarted** (onboarding not submitted), **Pending** (submitted, being verified), **Active** (payouts + transfers
  + charges), **ActionNeeded** (Stripe needs more / disabled). Read again on each visit and kept in sync from the v1
  `account.updated` webhook, which Stripe still sends for v2 accounts.
- Publishing: a listing in the USA / Ireland can't be published or relisted until the seller is Active (409 `PAYMENTS_NOT_READY`).
- Return from Stripe: `…/payments/stripe/onboarding-return` (a page that opens `shopcarparts://payments`); an expired link goes
  to `…/onboarding-refresh` ("open Payments in the app again" – no new link without sign-in).

## 5. Webhooks

`POST /payments/stripe/webhook` (Gateway → `api/payments/stripe/webhook`, no sign-in): signature checked with
`Stripe:WebhookSecret` (`EventUtility.ConstructEvent`, 5-minute tolerance) → 400 if wrong; objects without
`app=shopcarparts` → ignored (200) – disputes carry no metadata, so a dispute counts as ours when its charge is one of our
payments; event id already in `PaymentWebhookEvents` → "duplicate" (200); otherwise handled and stored.

Handled: `account.updated`, `payment_intent.succeeded` / `payment_failed` / `canceled`, `charge.refunded` (refunds made in the
Dashboard), `charge.dispute.created` / `updated` / `closed`, `transfer.reversed`.

Locally: `bash stripe-listen.sh` (`--forward-to` for platform events, `--forward-connect-to` for connected accounts' events).

## 6. Tables (AuctionService; migrations `Payments`, `PaymentsBuyers`, `PaymentsPayouts`)

| Table | What |
|---|---|
| `SellerPaymentAccounts` | Seller, provider, `acct_…`, country, chargesEnabled, payoutsEnabled, transfersActive, detailsSubmitted, status, requirements due |
| `BuyerPaymentCustomers` | Buyer → `cus_…` (saved cards) |
| `Payments` | Order / reservation, buyer, seller, `pi_…`, `ch_…`, currency, item / shipping / total / commission / refunded (cents), commission %, status, transfer group, receipt URL, card, balance amount / currency / exchange rate, dispute id / status / reason |
| `PaymentTransfers` | Order, payment, seller, `acct_…`, `tr_…`, amount + currency sent (USD), the seller's share + currency, arrived amount + currency, reversed amount, status (Pending / Frozen / Transferred / Reversed / Failed / Cancelled), attempts |
| `PaymentRefunds` | Payment, order, `re_…`, amount, currency, reason, status, by whom (seller, admin, "stripe" for Dashboard refunds) |
| `PaymentWebhookEvents` | `evt_…`, type, outcome, processed at (idempotency) |
| `StockReservations` | Buy now: listing, buyer, quantity, expires at, status (Active / Converted / Released), order |
| `SecondChanceOffers` | Listing, bidder, their highest bid, round 1–3, status, deadline, order |
| `NonPaymentStrikes` | User, listing, order, created, cleared (by an admin) |
| `Orders` (new columns) | Payment status / due / paid / cancelled, reminders, second-chance round; `AutoCompleteAt`, `OnHoldSince`, `HoldReason` |

## 7. Buyer payment (Step 2)

- **Who is charged:** the platform (separate charges and transfers). PaymentIntent: amount from the backend only (item
  price or winning bid × quantity + shipping, cents), `transfer_group=order_<orderId>`, `metadata.app=shopcarparts`,
  `metadata.payment_id`, idempotency key `pi-<paymentId>`, card only, receipt e-mail. Stripe customer + ephemeral key so the
  Payment Sheet can offer saved cards.
- **Paid = webhook:** `payment_intent.succeeded` (signature checked, `app` tag, handled once, amount and currency must match)
  marks the payment Succeeded and the order Paid. The app only polls `GET /payments/:id` to show the result.
- **Buy now:** `POST /payments/checkout/buy-now` reserves the units for 15 min (`QuantityReserved`, a concurrency token, so
  two buyers can't hold the last unit). The order is created by the webhook. Declined → `payment_failed` (message shown,
  the buyer can retry until the reservation ends). Leaving / time up → PaymentIntent cancelled, units released. A late
  success after that (units gone) is refunded.
- **Auctions:** the finish handler creates the order `AwaitingPayment` with `PaymentDueAt` = end + 48 h (USA / Ireland
  only; India / Nigeria keep the old flow). The buyer chooses pickup / shipping, then pays. Reminders at half time and 2 h
  before. Not paid → order Cancelled "Not paid in time" + strike → offer to the next-highest bidder at their own highest
  bid, same window; up to 3 rounds, then `UnsoldNotPaid` (relist).
- **Strikes:** 2 in 12 months → BidService refuses bids (gRPC `GetBidderStanding`); such bidders get no offers. Admin clears.
- **Job:** `PaymentJobs` every 30 s (`JobIntervalSeconds`); each step checks the current state first, so a restart repeats nothing.

## 8. After the payment (Step 3)

Code: `Payments/SellerPayouts.cs` (`SellerPayoutService`), wired into `OrdersController` (handover / completion) and
`PaymentJobs`.

- **Payout = transfer on completion.** When the order becomes Completed (the buyer confirms; or the job, `AutoCompleteDays`
  after Picked up / Shipped; or support), a `PaymentTransfer` is created and sent: amount = item + shipping − commission −
  refunds, `source_transaction` = the buyer's charge (allowed while the charge's funds are still pending),
  `transfer_group=order_<id>`, `metadata.app`, idempotency key `tr-<transferId>`. EUR → USD at the charge's rate (§3).
- **Not sent yet → retried:** seller not Active ("waiting for your payment setup") or a Stripe error → Pending; the job retries
  after 1, 4, 9 … minutes (at most hourly). Admin: `POST /payments/admin/transfers/:id/retry`.
- **Auto-complete:** set when the seller marks Picked up / Shipped (`Order.AutoCompleteAt`). Never before the handover, never
  while on hold. The buyer sees "Completes automatically on 23 Oct …"; the seller "You'll receive $118.80 when the buyer
  confirms, or automatically on 23 Oct".
- **Holds:** the buyer can **report a problem** (paid, not completed) → on hold: no auto-complete, no payout; the seller is told.
  The buyer confirming completion ends their own problem. Support: hold / release (`complete: true` completes now and pays).
- **Refunds** (to the original card; `refund-<rowId>` idempotency key): the **seller** can cancel and refund in full before pickup /
  shipping; **support** any amount at any time; refunds made in **Stripe's Dashboard** are recorded from `charge.refunded`.
  Before the payout the seller's share shrinks (0 → no payout); after it, the seller's part of the refund is taken back
  (transfer reversal at the payout's rate). A full refund cancels an open order ("Refunded – …").
- **Disputes:** created → payment Disputed, order on hold, payout Frozen (seller told). Closed **won** → hold released, payout
  sent if completed. **Lost** → no payout; if it was already paid out, support decides (`POST /payments/admin/transfers/:id/reverse`).
- **Receipts:** the buyer's receipt (item, shipping, total, card, refunds, Stripe's receipt page) and the seller's payout
  statement (item, shipping, commission, share, payout status) – `GET /payments/orders/:id/receipt`; the order card links
  Stripe's receipt page. Stripe e-mails the receipt to `receipt_email` in live mode (in test mode it only does when sent from the
  Dashboard).
- **Admin:** list payments (filter by status / user), one order's payment with the Stripe ids, refund, hold, release, retry and
  reverse a payout, clear strikes.
- **Demo:** `Payments:AutoCompleteMinutes` replaces the 7 days; `bmwapi/shorten-payment-deadline.sh` also shortens waiting
  auto-completes.

## 9. Switching to another Stripe account

`bmwapi/clear-stripe-test-data.sh --yes` clears the local Stripe data (accounts, customers, payments, transfers, refunds,
webhook events, open reservations); new keys in user-secrets; `stripe-listen.sh`; restart; sellers onboard again
(`bmwapi/stripe-onboarding-link.sh <seller>` prints a fresh onboarding link). RUN_LOCAL.md → "Payments (Stripe test mode)".
