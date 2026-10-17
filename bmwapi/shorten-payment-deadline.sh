#!/usr/bin/env bash
# DEMO / TEST ONLY: makes waiting auction payments and second-chance offers run out soon, so you can watch
# the reminders, "Not paid in time", the next-bidder offers and the strikes without waiting 48 hours – and makes
# picked-up / shipped orders complete by themselves soon (the seller's payout) instead of after 7 days.
#
#   cd /c/Bmwmobile/bmwapi && bash shorten-payment-deadline.sh            # every waiting payment/offer: 3 minutes left
#   bash shorten-payment-deadline.sh 5                                    # 5 minutes left
#   bash shorten-payment-deadline.sh 2 <orderId>                          # only that order (and its open offer)
#
# The background job (every Payments:JobIntervalSeconds, 30 s by default) then sends the last reminder
# ("pay within 3 min …") and, when the time is up, cancels the order, adds the strike and offers the item to the
# next bidder; picked-up / shipped orders complete and the seller is paid. For new orders/offers to start short too,
# restart the backend with Payments__PaymentDeadlineMinutes=3 (and Payments__AutoCompleteMinutes=3).
# It only touches the LOCAL database (no Stripe call).
set -euo pipefail

MINUTES="${1:-3}"
ORDER="${2:-}"
[[ "$MINUTES" =~ ^[0-9]+$ ]] || { echo "Usage: bash shorten-payment-deadline.sh [minutes] [orderId]"; exit 1; }
[[ -z "$ORDER" || "$ORDER" =~ ^[0-9a-fA-F-]{36}$ ]] || { echo "orderId must be a GUID"; exit 1; }

CONTAINER="${POSTGRES_CONTAINER:-bmwapi-postgres-1}"
docker exec "$CONTAINER" true 2>/dev/null || { echo "Postgres container '$CONTAINER' isn't running (start Docker / ./start-backend.sh)."; exit 1; }

ORDER_FILTER=""
OFFER_FILTER=""
if [ -n "$ORDER" ]; then
  ORDER_FILTER="AND \"Id\" = '$ORDER'"
  OFFER_FILTER="AND \"OrderId\" = '$ORDER'"
fi

docker exec -i "$CONTAINER" psql -v ON_ERROR_STOP=1 -q -t -U postgres -d auctions <<SQL
BEGIN;
-- Orders waiting for the buyer's payment (PaymentStatus AwaitingPayment).
WITH changed AS (
  UPDATE "Orders" SET "PaymentDueAt" = now() + interval '$MINUTES minutes'
  WHERE "PaymentStatus" = 'AwaitingPayment' AND "PaymentDueAt" IS NOT NULL $ORDER_FILTER
  RETURNING 1)
SELECT 'orders waiting for payment: ' || count(*) FROM changed;
-- Open second-chance offers (the offer's end is also the order's payment deadline once accepted).
WITH changed AS (
  UPDATE "SecondChanceOffers" SET "ExpiresAt" = now() + interval '$MINUTES minutes'
  WHERE "Status" = 'Offered' $OFFER_FILTER
  RETURNING 1)
SELECT 'open second-chance offers:  ' || count(*) FROM changed;
-- Paid, picked up / shipped orders waiting to complete by themselves (not on hold).
WITH changed AS (
  UPDATE "Orders" SET "AutoCompleteAt" = now() + interval '$MINUTES minutes'
  WHERE "AutoCompleteAt" IS NOT NULL AND "OnHoldSince" IS NULL AND "Status" <> 2 $ORDER_FILTER
  RETURNING 1)
SELECT 'orders to auto-complete:    ' || count(*) FROM changed;
COMMIT;
SQL
echo "Done: they now end in $MINUTES min. Pull down to refresh the order in the app."
