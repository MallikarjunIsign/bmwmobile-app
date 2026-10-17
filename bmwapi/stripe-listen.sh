#!/usr/bin/env bash
# Forwards Stripe TEST-mode webhooks to the local backend (through the Gateway), using the key in user-secrets.
#   cd /c/Bmwmobile/bmwapi && bash stripe-listen.sh        (keep this terminal open while testing payments)
# Needs the Stripe CLI (RUN_LOCAL.md, "Payments (Stripe test mode)") and Stripe:SecretKey in AuctionService user-secrets.
# The first time (or when the CLI's secret changes) it saves the printed whsec_… as Stripe:WebhookSecret – restart
# AuctionService afterwards. Keys are never printed.
#
# Both flags are needed: --forward-to gets the platform's events (payments, refunds, disputes, transfers),
# --forward-connect-to the connected accounts' events (account.updated). Every object the app creates is tagged
# metadata app=shopcarparts; events without the tag (e.g. Stripe's sample objects from `stripe trigger`) are ignored.
# This doesn't change any Stripe account settings.
set -euo pipefail
cd "$(dirname "$0")"

STRIPE="${STRIPE_CLI:-stripe}"
command -v "$STRIPE" > /dev/null || { echo "Stripe CLI not found (see RUN_LOCAL.md). Or set STRIPE_CLI=/path/to/stripe.exe"; exit 1; }
URL="${WEBHOOK_URL:-http://localhost:6001/payments/stripe/webhook}"

secret_value() {
  dotnet user-secrets list --project src/AuctionService 2>/dev/null | sed -n "s/^$1 = //p" | tr -d '\r'
}
KEY="$(secret_value 'Stripe:SecretKey')"
[ -n "$KEY" ] || { echo "Stripe:SecretKey is not in user-secrets (RUN_LOCAL.md, \"Payments (Stripe test mode)\")."; exit 1; }
case "$KEY" in sk_test_*|rk_test_*) ;; *) echo "Only Stripe TEST keys (sk_test_…) are allowed."; exit 1 ;; esac

# The CLI's webhook signing secret for this key; saved to user-secrets when it differs (not printed).
WHSEC="$("$STRIPE" listen --api-key "$KEY" --print-secret)"
if [ "$(secret_value 'Stripe:WebhookSecret')" != "$WHSEC" ]; then
  dotnet user-secrets set "Stripe:WebhookSecret" "$WHSEC" --project src/AuctionService > /dev/null
  echo "Saved the Stripe CLI's webhook secret as Stripe:WebhookSecret – restart AuctionService (./start-backend.sh) to use it."
fi

# Only the events the backend handles (newer CLIs require the list).
EVENTS="account.updated,payment_intent.succeeded,payment_intent.payment_failed,payment_intent.canceled,charge.refunded,charge.dispute.created,charge.dispute.updated,charge.dispute.closed,transfer.created,transfer.reversed"

echo "Forwarding Stripe test webhooks to $URL (Ctrl+C to stop)"
# The CLI prints the signing secret when it starts: masked here, like the key.
"$STRIPE" listen --api-key "$KEY" --events "$EVENTS" --forward-to "$URL" --forward-connect-to "$URL" 2>&1 \
  | sed -u -E 's/whsec_[A-Za-z0-9]+/whsec_****/g; s/(sk|rk)_test_[A-Za-z0-9]+/\1_test_****/g'
