#!/usr/bin/env bash
# Runs the search / filter UI test (e2e/search-filters.yaml) on the emulator, as the test buyer and the test seller,
# then "Search location" (e2e/place-search.yaml: add "Kukatpally" as the test buyer in Hyderabad), then the radius test (e2e/radius.yaml: who sees which listings, radius 200 → 25 mi) as the test buyer in Houston.
# Needs: the backend (./start-backend.sh, started with the e2e seller seeded – see pre-demo-check.sh), Metro, an emulator,
# and Maestro (~/.maestro/maestro/bin/maestro – see RUN_LOCAL.md "Pre-demo check").
# Test accounts come from bmwapi/.run/e2e.env (git-ignored, created by pre-demo-check.sh). Passwords are never printed.
set -uo pipefail

cd "$(dirname "$0")"
E2E_DIR="$(pwd)"
ENV_FILE="$E2E_DIR/../../bmwapi/.run/e2e.env"
GATEWAY="${GATEWAY_URL:-http://localhost:6001}"
MAESTRO="${MAESTRO:-$HOME/.maestro/maestro/bin/maestro}"
export MAESTRO_CLI_NO_ANALYTICS=1 MAESTRO_CLI_ANALYSIS_NOTIFICATION_DISABLED=true

[ -f "$ENV_FILE" ] || { echo "Missing $ENV_FILE - run ./pre-demo-check.sh once (it creates the test accounts)."; exit 1; }
set -a; . "$ENV_FILE"; set +a
command -v "$MAESTRO" > /dev/null || [ -x "$MAESTRO" ] || { echo "Maestro not found at $MAESTRO (see RUN_LOCAL.md)."; exit 1; }

# Every adb / Maestro call has a time limit: a hung emulator fails the test instead of hanging it.
[ "$(timeout 20 adb shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = 1 ] \
  || { echo "The emulator doesn't respond (adb). Cold boot it (Device Manager → ⋮ → Cold Boot Now) and run this again."; exit 1; }
# A Maestro left over from an interrupted run keeps its session file locked, and the next run waits forever.
bash "$E2E_DIR/stop-stale-maestro.sh"

# The e2e accounts start like new users: no saved locations, so the GPS location gets the default radius (200 km / 200 mi).
# (Only these git-ignored test accounts are touched.)
# On the database in use (local container or the cloud VM – DB_TARGET / the last start).
reset_locations() {
  bash "$E2E_DIR/../../bmwapi/db-psql.sh" identity -q -v email="$1" > /dev/null <<'SQL'
DELETE FROM "UserLocations" WHERE "UserId" IN (SELECT "Id" FROM "AspNetUsers" WHERE "NormalizedEmail" = upper(:'email'));
UPDATE "AspNetUsers" SET "AutoUpdatePrimaryLocation" = true WHERE "NormalizedEmail" = upper(:'email');
SQL
}
reset_locations "$E2E_BUYER_EMAIL"; reset_locations "$E2E_SELLER_EMAIL"

# The demo listings are around Hyderabad: put the emulator there.
timeout 20 adb emu geo fix 78.4867 17.3850 > /dev/null
timeout 20 adb reverse tcp:8081 tcp:8081 > /dev/null

# Expected counts, asked from the API with the same filters the app sends (Parts, the default 200 km around Hyderabad).
NEAR="itemType=Part&lat=17.385&lon=78.4867&country=IN&radiusKm=200&pageSize=1"
count() { curl -sf "$GATEWAY/search/listings?$NEAR&$1" | sed -E 's/.*"totalCount":([0-9]+).*/\1/'; }
BUMPERS=$(count "partType=Bumper")
LIGHTING=$(count "category=Lighting")
if [ -z "$BUMPERS" ] || [ -z "$LIGHTING" ] || [ "$BUMPERS" = 0 ] || [ "$LIGHTING" = 0 ]; then
  echo "The search API has no bumpers / lighting parts near Hyderabad (bumpers=$BUMPERS, lighting=$LIGHTING)."
  echo "Is the backend running with the demo data? ./start-backend.sh"
  exit 1
fi
echo "API: $BUMPERS bumper(s), $LIGHTING lighting part(s) near Hyderabad"

OUT="$E2E_DIR/../.e2e-results"
mkdir -p "$OUT"
failed=0
for who in BUYER SELLER; do
  email_var="E2E_${who}_EMAIL"; password_var="E2E_${who}_PASSWORD"
  echo "== Search UI test as the test ${who,,}"
  rm -f "$OUT/search-${who,,}.xml"
  (cd "$OUT" && timeout 900 "$MAESTRO" test \
      -e EMAIL="${!email_var}" -e PASSWORD="${!password_var}" -e ROLE="$who" -e BUMPERS="$BUMPERS" -e LIGHTING="$LIGHTING" \
      --format junit --output "$OUT/search-${who,,}.xml" \
      "$E2E_DIR/search-filters.yaml") 2>&1 | grep -vi "password" | grep -E "COMPLETED|FAILED|Element|Assert|not found|Passed|Failed" | tail -60
  # No result file = Maestro didn't finish (stopped after 15 minutes, or couldn't reach the emulator).
  if [ "${PIPESTATUS[0]}" -ne 0 ] || [ ! -f "$OUT/search-${who,,}.xml" ] || grep -q "<failure" "$OUT/search-${who,,}.xml"; then
    failed=1; echo "   -> FAILED (screenshots: ~/.maestro/tests/<latest>)"
  else
    echo "   -> PASSED"
  fi
done

# ── "Search location" (Google Places): buyer in Hyderabad adds "Kukatpally" ──
# Needs GoogleMaps:ApiKey in IdentityService user-secrets (else the city list is used and this test fails on purpose).
echo "== Search location UI test as the test buyer (Hyderabad)"
reset_locations "$E2E_BUYER_EMAIL"
bash "$E2E_DIR/stop-stale-maestro.sh"
rm -f "$OUT/place-search-buyer.xml"
(cd "$OUT" && timeout 900 "$MAESTRO" test \
    -e EMAIL="$E2E_BUYER_EMAIL" -e PASSWORD="$E2E_BUYER_PASSWORD" \
    --format junit --output "$OUT/place-search-buyer.xml" \
    "$E2E_DIR/place-search.yaml") 2>&1 | grep -vi "password" | grep -E "COMPLETED|FAILED|Element|Assert|not found|Passed|Failed" | tail -60
if [ "${PIPESTATUS[0]}" -ne 0 ] || [ ! -f "$OUT/place-search-buyer.xml" ] || grep -q "<failure" "$OUT/place-search-buyer.xml"; then
  failed=1; echo "   -> FAILED (screenshots: ~/.maestro/tests/<latest>)"
else
  echo "   -> PASSED"
fi
grep "Google Maps calls" "$E2E_DIR/../../bmwapi/.run/logs/identity.log" 2>/dev/null | tail -1 | sed 's/^/   /'

# ── Radius test (USA): buyer in Houston ──
timeout 20 adb emu geo fix -95.3698 29.7604 > /dev/null
reset_locations "$E2E_BUYER_EMAIL"
HOUSTON="itemType=Part&lat=29.7604&lon=-95.3698&country=US&pageSize=50"
search() { curl -sf "$GATEWAY/search/listings?$HOUSTON&radiusKm=$1"; }
total() { sed -E 's/.*"totalCount":([0-9]+).*/\1/'; }
R200=$(search 321.87); R25=$(search 40.23)
PARTS_200=$(echo "$R200" | total); PARTS_25=$(echo "$R25" | total)
# The demo listings this test needs: far shipping-only (Long Beach) always, far pickup-only (Round Rock) only at 200 mi.
if ! echo "$R200" | grep -q '"demo-us-la-alternator"' || ! echo "$R25" | grep -q '"demo-us-la-alternator"' \
   || ! echo "$R200" | grep -q '"demo-us-austin-radiator"' || echo "$R25" | grep -q '"demo-us-austin-radiator"'; then
  echo "The API doesn't show the far US demo listings as expected (Long Beach shipping-only, Round Rock pickup-only)."
  echo "Restart the backend once so the demo data and the search index are up to date: ./start-backend.sh"
  exit 1
fi
echo "API: $PARTS_200 part(s) within reach at 200 mi around Houston, $PARTS_25 at 25 mi"

echo "== Radius UI test as the test buyer (Houston)"
# The previous Maestro run may still hold its session file: stop it, or this run waits on the lock.
bash "$E2E_DIR/stop-stale-maestro.sh"
rm -f "$OUT/radius-buyer.xml"
(cd "$OUT" && timeout 900 "$MAESTRO" test \
    -e EMAIL="$E2E_BUYER_EMAIL" -e PASSWORD="$E2E_BUYER_PASSWORD" -e PARTS_200="$PARTS_200" -e PARTS_25="$PARTS_25" \
    --format junit --output "$OUT/radius-buyer.xml" \
    "$E2E_DIR/radius.yaml") 2>&1 | grep -vi "password" | grep -E "COMPLETED|FAILED|Element|Assert|not found|Passed|Failed" | tail -60
if [ "${PIPESTATUS[0]}" -ne 0 ] || [ ! -f "$OUT/radius-buyer.xml" ] || grep -q "<failure" "$OUT/radius-buyer.xml"; then
  failed=1; echo "   -> FAILED (screenshots: ~/.maestro/tests/<latest>)"
else
  echo "   -> PASSED"
fi

# Back to Hyderabad for the other demos.
timeout 20 adb emu geo fix 78.4867 17.3850 > /dev/null
exit $failed
