#!/usr/bin/env bash
# TEST MODE: prints a fresh Stripe onboarding link for a seller's payment account, to open in the PC's browser
# (the same Stripe-hosted page the app opens from Profile → Payments). Links work once and expire after a few minutes.
#
#   cd /c/Bmwmobile/bmwapi && bash stripe-onboarding-link.sh <seller username>      e.g.  bash stripe-onboarding-link.sh stripe
#
# The seller needs a payment account first (created when an admin makes them a seller, or on their first visit to
# Profile → Payments). At the end Stripe returns to the Gateway's "Payment setup saved" page. The key is never printed.
set -euo pipefail
cd "$(dirname "$0")"

SELLER="${1:-}"
[[ "$SELLER" =~ ^[A-Za-z0-9._@-]{1,100}$ ]] || { echo "Usage: bash stripe-onboarding-link.sh <seller username>"; exit 1; }
BASE="${PUBLIC_BASE_URL:-http://localhost:6001}"

KEY="$(dotnet user-secrets list --project src/AuctionService 2>/dev/null | sed -n 's/^Stripe:SecretKey = //p' | tr -d '\r')"
[ -n "$KEY" ] || { echo "Stripe:SecretKey is not in user-secrets (RUN_LOCAL.md, \"Payments (Stripe test mode)\")."; exit 1; }
case "$KEY" in sk_test_*|rk_test_*) ;; *) echo "Only Stripe TEST keys (sk_test_…) are allowed."; exit 1 ;; esac

CONTAINER="${POSTGRES_CONTAINER:-bmwapi-postgres-1}"
ACCT="$(docker exec "$CONTAINER" psql -U postgres -d auctions -tA -c "SELECT \"ProviderAccountId\" FROM \"SellerPaymentAccounts\" WHERE \"Seller\" = '$SELLER'" | tr -d '\r')"
[ -n "$ACCT" ] || { echo "Seller '$SELLER' has no payment account yet (Profile → Payments in the app creates it)."; exit 1; }

RESPONSE="$(curl -s -u "$KEY:" https://api.stripe.com/v1/account_links \
  -d account="$ACCT" -d type=account_onboarding \
  -d return_url="$BASE/payments/stripe/onboarding-return" -d refresh_url="$BASE/payments/stripe/onboarding-refresh")"
URL="$(printf '%s' "$RESPONSE" | sed -n 's/.*"url": *"\([^"]*\)".*/\1/p')"
if [ -z "$URL" ]; then
  echo "Stripe refused: $(printf '%s' "$RESPONSE" | sed -n 's/.*"message": *"\([^"]*\)".*/\1/p')"
  exit 1
fi
echo "Seller $SELLER ($ACCT) – open within a few minutes:"
echo "$URL"
