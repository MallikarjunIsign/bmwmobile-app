#!/usr/bin/env bash
# Clears this app's LOCAL Stripe test data, so sellers can redo onboarding on a new Stripe account / sandbox:
#   - sellers' payment accounts (acct_… IDs and statuses)          -> table "SellerPaymentAccounts"
#   - buyers' Stripe customers (cus_… IDs, saved cards)             -> "BuyerPaymentCustomers"
#   - payments, transfers and refunds of test orders (pi_/tr_/re_)  -> "Payments", "PaymentTransfers", "PaymentRefunds"
#   - handled webhook event IDs (evt_…)                             -> "PaymentWebhookEvents"
#   - open Buy-now reservations                                     -> "StockReservations"
# Listings, orders, users, chats and reviews stay. Nothing is deleted at Stripe (do that in the Stripe Dashboard if
# needed – the old account is the shared one, so only delete objects with metadata app=shopcarparts).
#
#   cd /c/Bmwmobile/bmwapi && bash clear-stripe-test-data.sh --yes
# Then restart the backend with the NEW keys in user-secrets (RUN_LOCAL.md, "Switch to another Stripe account").
set -euo pipefail

if [ "${1:-}" != "--yes" ]; then
  echo "This deletes the local Stripe test data (payment accounts, customers, payments, transfers, refunds, webhook events)."
  echo "Run again with --yes to do it:  bash clear-stripe-test-data.sh --yes"
  exit 1
fi

CONTAINER="${POSTGRES_CONTAINER:-bmwapi-postgres-1}"
docker exec "$CONTAINER" true 2>/dev/null || { echo "Postgres container '$CONTAINER' isn't running (start Docker / ./start-backend.sh)."; exit 1; }

docker exec -i "$CONTAINER" psql -v ON_ERROR_STOP=1 -q -U postgres -d auctions <<'SQL'
BEGIN;
SELECT 'seller payment accounts: ' || count(*) FROM "SellerPaymentAccounts";
SELECT 'buyer customers:         ' || count(*) FROM "BuyerPaymentCustomers";
SELECT 'payments:                ' || count(*) FROM "Payments";
SELECT 'transfers:               ' || count(*) FROM "PaymentTransfers";
SELECT 'refunds:                 ' || count(*) FROM "PaymentRefunds";
DELETE FROM "PaymentRefunds";
DELETE FROM "PaymentTransfers";
DELETE FROM "Payments";
DELETE FROM "BuyerPaymentCustomers";
DELETE FROM "SellerPaymentAccounts";
DELETE FROM "PaymentWebhookEvents";
UPDATE "StockReservations" SET "Status" = 'Released' WHERE "Status" = 'Active';
-- The released units are for sale again.
UPDATE "Auctions" SET "QuantityReserved" = 0 WHERE "QuantityReserved" <> 0;
COMMIT;
SQL
echo "Local Stripe test data cleared. Sellers in the USA / Ireland set up payments again (Profile → Payments)."
