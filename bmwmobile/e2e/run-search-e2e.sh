#!/usr/bin/env bash
# Runs the search / filter UI test (e2e/search-filters.yaml) on the emulator, as the test buyer and the test seller.
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

# The demo listings are around Hyderabad: put the emulator there.
timeout 20 adb emu geo fix 78.4867 17.3850 > /dev/null
timeout 20 adb reverse tcp:8081 tcp:8081 > /dev/null

# Expected counts, asked from the API with the same filters the app sends (Parts, 60 km around Hyderabad).
NEAR="itemType=Part&lat=17.385&lon=78.4867&radiusKm=60&pageSize=1"
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
exit $failed
