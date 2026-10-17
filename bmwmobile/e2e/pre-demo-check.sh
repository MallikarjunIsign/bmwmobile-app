#!/usr/bin/env bash
# Pre-demo check: starts everything from clean and proves search + filters work, the way a user uses them.
#   cd /c/Bmwmobile/bmwmobile && bash e2e/pre-demo-check.sh
# 1. stops the .NET services and Metro (Docker data is kept)
# 2. backend search tests (in-process, real Meilisearch in a test container: every filter, settings after a restart)
# 3. starts the backend with ./start-backend.sh (fresh build) + an e2e test seller; creates the e2e test buyer
# 4. live API tests: app → Gateway → Search → Meilisearch on the running backend, with the demo data
# 5. app unit tests (jest)
# 6. Metro with a reset cache, the emulator, then the UI test on the emulator (Maestro) as buyer and seller:
#    cleared app data → log in → type "bumper" → tap the suggestion → only bumpers; category suggestion; category tile;
#    Market shows the same; counts match the API.
#    Then "Search location" as the buyer in Hyderabad: Home → add a location → type "Kukatpally" → pick the Google
#    suggestion ("powered by Google") → save → Home shows "Kukatpally, Hyderabad · 200 km" (needs GoogleMaps:ApiKey).
#    Then the radius test as the buyer in Houston (USA): location shows 200 mi → the far shipping-only listing is shown
#    with "Ships to you" → radius 25 mi on Home → the far pickup-only listing disappears, the shipping one stays
#    (counts match the API at 200 and 25 mi) → back to 200 mi
# Ends with PASSED or FAILED per step. Logs: bmwmobile/.e2e-results/
set -uo pipefail

cd "$(dirname "$0")/.."
APP_DIR="$(pwd)"
API_DIR="$APP_DIR/../bmwapi"
OUT="$APP_DIR/.e2e-results"
ENV_FILE="$API_DIR/.run/e2e.env"
AVD="${AVD:-Pixel_8}"
EMULATOR="${LOCALAPPDATA:-$HOME/AppData/Local}/Android/Sdk/emulator/emulator.exe"
mkdir -p "$OUT" "$API_DIR/.run"
results=()
step() { echo; echo "== $*"; }
record() { if [ "$2" -eq 0 ]; then results+=("PASSED  $1"); else results+=("FAILED  $1"); fi; }
finish() {
  echo; echo "================ Pre-demo check ================"
  printf '%s\n' "${results[@]}"
  if printf '%s\n' "${results[@]}" | grep -q '^FAILED'; then echo "=> NOT READY for the demo (logs: $OUT)"; exit 1; fi
  echo "=> READY for the demo"; exit 0
}

step "0/6 Disk space (stops below 3 GB free)"
bash "$API_DIR/check-disk.sh" || { record "Disk space (at least 3 GB free)" 1; finish; }

step "1/6 Stopping the backend services and Metro (data is kept)"
bash "$API_DIR/stop-backend.sh" --keep-docker
for pid in $(netstat -ano | grep -E "TCP +[^ ]+:8081 .*LISTENING" | awk '{print $NF}' | sort -u); do
  taskkill //PID "$pid" //T //F > /dev/null 2>&1 && echo "  Metro stopped (PID $pid)"
done
docker info > /dev/null 2>&1 || { echo "Docker is not running. Start Docker Desktop first."; record "Docker" 1; finish; }

# Test accounts (only on this PC, git-ignored). Passwords are random and never printed.
if [ ! -f "$ENV_FILE" ]; then
  pw() { echo "E2e$(openssl rand -hex 6)!x"; }
  printf 'E2E_BUYER_EMAIL=e2e.buyer@shopcarparts.test\nE2E_BUYER_PASSWORD=%s\nE2E_SELLER_EMAIL=e2e.seller@shopcarparts.test\nE2E_SELLER_PASSWORD=%s\nE2E_ADMIN_EMAIL=e2e.admin@shopcarparts.test\nE2E_ADMIN_PASSWORD=%s\n' "$(pw)" "$(pw)" "$(pw)" > "$ENV_FILE"
  echo "  Created test accounts file $ENV_FILE"
fi
set -a; . "$ENV_FILE"; set +a

step "2/6 Backend search tests (filters, counts, index settings after a restart)"
(cd "$API_DIR" && DOTNET_ROLL_FORWARD=Major dotnet test tests/SearchService.Tests --nologo -v q > "$OUT/backend-tests.log" 2>&1)
rc=$?; grep -E "Passed!|Failed!" "$OUT/backend-tests.log"; record "Backend search tests" $rc

step "3/6 Starting the backend (fresh build) with the e2e test seller and admin"
# Not piped: the background services would keep a pipe open. The seed variables live only in this run.
(cd "$API_DIR" && Seed__SellerEmail="$E2E_SELLER_EMAIL" Seed__SellerPassword="$E2E_SELLER_PASSWORD" \
  Seed__AdminEmail="${E2E_ADMIN_EMAIL:-}" Seed__AdminPassword="${E2E_ADMIN_PASSWORD:-}" bash ./start-backend.sh > "$OUT/start-backend.log" 2>&1)
rc=$?; cat "$OUT/start-backend.log"; record "Backend started" $rc
[ $rc -eq 0 ] || finish
grep -q "Search index settings applied" "$API_DIR/.run/logs/search.log" && echo "  Search: index settings applied" \
  || { echo "  Search: index settings NOT applied (see .run/logs/search.log)"; record "Search index settings" 1; }
code=$(curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:5001/api/account/register -H 'Content-Type: application/json' \
  -d "{\"fullName\":\"E2E Buyer\",\"email\":\"$E2E_BUYER_EMAIL\",\"phoneNumber\":\"+919000000002\",\"password\":\"$E2E_BUYER_PASSWORD\",\"acceptTerms\":true}")
echo "  test buyer: $([ "$code" = 201 ] && echo created || echo "already there ($code)")"
# Wait until the search index has the demo data (re-indexing runs in the background after start).
for _ in $(seq 1 30); do
  n=$(curl -sf "http://localhost:6001/search/listings?itemType=Part&lat=17.385&lon=78.4867&radiusKm=60&pageSize=1" | sed -E 's/.*"totalCount":([0-9]+).*/\1/')
  [ "${n:-0}" -gt 0 ] && break; sleep 2
done

step "4/6 Live API tests through the Gateway (http://localhost:6001)"
(cd "$API_DIR" && GATEWAY_URL=http://localhost:6001 DOTNET_ROLL_FORWARD=Major dotnet test tests/SearchService.Tests --no-build --nologo \
  --filter "Category=Live" -v q > "$OUT/gateway-tests.log" 2>&1)
rc=$?; grep -E "Passed!|Failed!|Failed " "$OUT/gateway-tests.log"; record "Live Gateway search tests" $rc

step "5/6 App unit tests"
(npx jest > "$OUT/jest.log" 2>&1); rc=$?; grep -E "^Tests:" "$OUT/jest.log"; record "App unit tests" $rc

step "6/6 Metro (reset cache), emulator, UI test"
(npm start -- --reset-cache > "$OUT/metro.log" 2>&1 &)
for _ in $(seq 1 60); do curl -s http://localhost:8081/status 2>/dev/null | grep -q running && break; sleep 2; done
curl -s http://localhost:8081/status | grep -q running && echo "  Metro ready" || { echo "  Metro did not start (see $OUT/metro.log)"; record "Metro" 1; finish; }
# After --reset-cache the first bundle takes minutes: build it now, so the app opens straight away in the UI test.
echo "  Building the JS bundle (first time after --reset-cache)…"
curl -s -o /dev/null -m 600 "http://localhost:8081/index.bundle?platform=android&dev=true&minify=false" \
  && echo "  bundle ready" || echo "  bundle not built yet (the UI test waits for it)"
# Every adb call has a time limit: a hung emulator must fail the check, not hang it.
booted() { [ "$(timeout 20 adb shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = 1 ]; }
if ! booted; then
  bash "$API_DIR/check-disk.sh" || { record "Disk space before the emulator (at least 3 GB free)" 1; finish; }
  # -no-snapshot-save: closing the emulator doesn't write a multi-GB snapshot to the disk.
  echo "  Starting the emulator $AVD (restarting adb)"
  timeout 20 adb kill-server > /dev/null 2>&1; timeout 20 adb start-server > /dev/null 2>&1
  timeout 20 adb devices | grep -qE "emulator-[0-9]+\s+device" || ("$EMULATOR" -avd "$AVD" -no-boot-anim -no-snapshot-load -no-snapshot-save > "$OUT/emulator.log" 2>&1 &)
  for _ in $(seq 1 90); do booted && break; sleep 3; done
fi
if ! booted; then
  echo "  The emulator doesn't respond. Close it and cold boot it (Device Manager → ⋮ → Cold Boot Now), then run this again."
  record "Emulator" 1; finish
fi
echo "  emulator ready"
# The UI test clears the app's data itself when it starts the app; this is only a head start.
timeout 60 adb shell pm clear com.bidmywork.app > /dev/null 2>&1 && echo "  app data cleared"
bash "$APP_DIR/e2e/run-search-e2e.sh"; record "UI tests on the emulator (search: buyer + seller; search location: Kukatpally; radius: buyer in Houston)" $?

finish
