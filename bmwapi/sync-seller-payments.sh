#!/usr/bin/env bash
# Links every seller's existing Stripe account in the database the backend is using NOW (DB_TARGET=local or cloud)
# and reads each status again from Stripe. Creates nothing at Stripe. Run it after switching databases, e.g. when a
# seller finished payment setup on the other database and shows "Not started" here.
#
#   cd /c/Bmwmobile/bmwapi && bash sync-seller-payments.sh
#
# Needs the running backend and the e2e admin from .run/e2e.env (created by e2e/pre-demo-check.sh; never printed).
set -uo pipefail
cd "$(dirname "$0")"

ENV_FILE=.run/e2e.env
IDENTITY="${IDENTITY_URL:-http://localhost:5001}"
GATEWAY="${GATEWAY_URL:-http://localhost:6001}"
[ -f "$ENV_FILE" ] || { echo "Missing $ENV_FILE - run bmwmobile/e2e/pre-demo-check.sh once."; exit 1; }
set -a; . "$ENV_FILE"; set +a
[ -n "${E2E_ADMIN_EMAIL:-}" ] || { echo "No E2E_ADMIN_EMAIL in $ENV_FILE."; exit 1; }

json() { python -I -c "import sys,json; d=json.load(sys.stdin); print($1)"; }
# Admin token like the app: email + password, then the test-mode code (returned in Development).
first=$(curl -s -m 60 -X POST "$IDENTITY/connect/token" -d "grant_type=bmw_password&client_id=mobileApp&scope=openid%20IdentityServerApi" \
  --data-urlencode "username=$E2E_ADMIN_EMAIL" --data-urlencode "password=$E2E_ADMIN_PASSWORD" -d "device_id=sync-script")
challenge=$(echo "$first" | json "d.get('challengeId','')" 2>/dev/null)
otp=$(echo "$first" | json "d.get('devOtp') or ''" 2>/dev/null)
token=$(curl -s -m 60 -X POST "$IDENTITY/connect/token" -d "grant_type=bmw_otp&client_id=mobileApp&scope=openid%20IdentityServerApi" \
  -d "challenge_id=$challenge" -d "otp=$otp" -d "device_id=sync-script" | json "d.get('access_token','')" 2>/dev/null)
[ -n "$token" ] || { echo "Couldn't sign in as the e2e admin (is the backend running?)."; exit 1; }

echo "Database: $(cat .run/db-target 2>/dev/null || echo "?")"
response=$(curl -s -m 180 -w '\n%{http_code}' -X POST "$GATEWAY/payments/admin/seller-accounts/sync" -H "Authorization: Bearer $token")
code=$(echo "$response" | tail -1); body=$(echo "$response" | head -n -1)
[ "$code" = 200 ] || { echo "Sync failed (HTTP $code): $body"; exit 1; }
echo "$body" | python -I -c "
import sys, json
rows = json.load(sys.stdin)
print(f\"{'SELLER':<16} {'STATUS':<13} {'STRIPE ACCOUNT':<24} {'AT STRIPE':>9}  ACTION\")
for r in rows:
    print(f\"{r['seller']:<16} {r['status']:<13} {r['accountId'] or '-':<24} {r['accountsAtProvider']:>9}  {r['action']}\")
"
