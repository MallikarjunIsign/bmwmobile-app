#!/usr/bin/env bash
# "Want to sell?" end to end: the form on the emulator (signed out, from Sign up) → stored in the database → admins told
# (Identity log in test mode) → the admin API lists it. Needs the backend started by pre-demo-check.sh (e2e admin seeded),
# Metro and an emulator. Passwords come from bmwapi/.run/e2e.env and are never printed.
set -uo pipefail

cd "$(dirname "$0")"
E2E_DIR="$(pwd)"
API_DIR="$E2E_DIR/../../bmwapi"
ENV_FILE="$API_DIR/.run/e2e.env"
IDENTITY="${IDENTITY_URL:-http://localhost:5001}"
MAESTRO="${MAESTRO:-$HOME/.maestro/maestro/bin/maestro}"
export MAESTRO_CLI_NO_ANALYTICS=1 MAESTRO_CLI_ANALYSIS_NOTIFICATION_DISABLED=true
[ -f "$ENV_FILE" ] || { echo "Missing $ENV_FILE - run e2e/pre-demo-check.sh once."; exit 1; }
set -a; . "$ENV_FILE"; set +a
[ -n "${E2E_ADMIN_EMAIL:-}" ] || { echo "No E2E_ADMIN_EMAIL in $ENV_FILE - run e2e/pre-demo-check.sh once."; exit 1; }

EMAIL="applicant.$(date +%s)@shopcarparts.test"
OUT="$E2E_DIR/../.e2e-results"; mkdir -p "$OUT"
[ "$(timeout 20 adb shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = 1 ] \
  || { echo "The emulator doesn't respond (adb). Cold boot it and run this again."; exit 1; }
bash "$E2E_DIR/stop-stale-maestro.sh"
timeout 20 adb reverse tcp:8081 tcp:8081 > /dev/null

echo "== Seller form on the emulator ($EMAIL)"
rm -f "$OUT/seller-application.xml"
(cd "$OUT" && timeout 900 "$MAESTRO" test -e EMAIL="$EMAIL" --format junit --output "$OUT/seller-application.xml" "$E2E_DIR/seller-application.yaml") 2>&1 \
  | grep -E "Passed|Failed|FAILED" | tail -5
{ [ ! -f "$OUT/seller-application.xml" ] || grep -q "<failure" "$OUT/seller-application.xml"; } && { echo "   -> FAILED (screenshots: ~/.maestro/tests/<latest>)"; exit 1; }

echo "== Admin API: the request is stored and listed"
# Admin token like the app: email + password, then the test-mode code returned in Development.
first=$(curl -s -X POST "$IDENTITY/connect/token" -d "grant_type=bmw_password&client_id=mobileApp&scope=openid%20IdentityServerApi" \
  --data-urlencode "username=$E2E_ADMIN_EMAIL" --data-urlencode "password=$E2E_ADMIN_PASSWORD" -d "device_id=e2e-script")
json() { python -c "import sys,json; d=json.load(sys.stdin); print($1)"; }
challenge=$(echo "$first" | json "d.get('challengeId','')"); otp=$(echo "$first" | json "d.get('devOtp') or ''")
token=$(curl -s -X POST "$IDENTITY/connect/token" -d "grant_type=bmw_otp&client_id=mobileApp&scope=openid%20IdentityServerApi" \
  -d "challenge_id=$challenge" -d "otp=$otp" -d "device_id=e2e-script" | json "d.get('access_token','')")
[ -n "$token" ] || { echo "   -> FAILED: couldn't sign in as the e2e admin"; exit 1; }
found=$(curl -s -H "Authorization: Bearer $token" "$IDENTITY/api/admin/seller-applications?search=$EMAIL" \
  | json "' | '.join(f\"{a['status']} {a['fullName']} {a['phoneNumber']} {a['city']} {a['businessType']} {a['sells']} {a['businessName']}\" for a in d['items'])")
echo "   $found"
[ "$found" = "New E2E Applicant +919876543210 Hyderabad Shop Both E2E Auto Parts" ] || { echo "   -> FAILED: not stored as sent"; exit 1; }
grep -q "New seller application: E2E Applicant (E2E Auto Parts)" "$API_DIR/.run/logs/identity.log" \
  && echo "   admins notified (test mode: notice in the Identity log)" || { echo "   -> FAILED: no admin notice"; exit 1; }
echo "   -> PASSED"
