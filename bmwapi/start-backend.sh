#!/usr/bin/env bash
# Starts the whole backend for local testing (Windows Git Bash):
#   1. Docker: postgres, rabbitmq, meilisearch (data stays in the Docker volumes)
#   2. Builds the solution once
#   3. Starts each .NET service in the background, in the right order, and waits until it answers
# Logs: bmwapi/.run/logs/<service>.log   Stop with: ./stop-backend.sh  (keeps all data)
#
# Database: DB_TARGET=local (default) uses the postgres container on localhost:5433.
#           DB_TARGET=cloud ./start-backend.sh uses the Oracle VM through the SSH tunnel (./db-tunnel.sh,
#           started here when needed); the password comes from .run/cloud-db.env, never from appsettings.
# Search: Meilisearch is shared by both databases, so when DB_TARGET differs from the last start (.run/db-target) the
#         search index is re-synced from the database in use (RESYNC_SEARCH=1 forces it).
# Logs are rotated at 20 MB (3 files per service). Stops when less than 3 GB of disk is free.
set -uo pipefail

cd "$(dirname "$0")"
RUN_DIR="$(pwd)/.run"
LOG_DIR="$RUN_DIR/logs"
ROTATE="$(pwd)/rotate-log.sh"
mkdir -p "$LOG_DIR"

bash ./check-disk.sh || exit 1

# name | folder under src/ | port
SERVICES=(
  "identity|IdentityService|5001"
  "auction|AuctionService|7001"
  "search|SearchService|7002"
  "bid|BidService|7003"
  "notify|NotificationService|7004"
  "chat|ChatService|7005"
  "gateway|GatewayService|6001"
)

port_in_use() { netstat -ano | grep -E "TCP +(127\.0\.0\.1|0\.0\.0\.0|\[::1?\]):$1 .*LISTENING" > /dev/null; }

DB_TARGET="${DB_TARGET:-local}"
case "$DB_TARGET" in
  local) CONTAINERS="postgres rabbitmq meilisearch" ;;
  cloud)
    CONTAINERS="rabbitmq meilisearch"
    if [ ! -f "$RUN_DIR/cloud-db.env" ]; then
      echo "DB_TARGET=cloud needs $RUN_DIR/cloud-db.env - see RUN_LOCAL.md (cloud database)."
      exit 1
    fi
    set -a; . "$RUN_DIR/cloud-db.env"; set +a
    ;;
  *) echo "DB_TARGET must be local or cloud (got '$DB_TARGET')."; exit 1 ;;
esac

LAST_TARGET="$(cat "$RUN_DIR/db-target" 2>/dev/null | tr -d '\r\n')"
RESYNC=0
if [ "${RESYNC_SEARCH:-0}" = 1 ] || [ "$LAST_TARGET" != "$DB_TARGET" ]; then RESYNC=1; fi

# Cloud only: the connection string for one service, set as an environment variable (overrides appsettings).
# Keepalive: idle pooled connections stay alive through the SSH tunnel, so a request doesn't get a dead connection
# ("An error occurred using the connection …"). A few connections are kept open (Minimum Pool Size, 5 min idle
# lifetime): opening a new one through the tunnel costs several round trips (~2–4 s from India).
db_env() {
  [ "$DB_TARGET" = cloud ] || return 0
  local key db
  case "$1" in
    identity) key=DefaultConnection; db=identity ;;
    auction)  key=DefaultConnection; db=auctions ;;
    bid)      key=BidDbConnection;   db=bids ;;
    chat)     key=DefaultConnection; db=chat ;;
    *) return 0 ;;
  esac
  echo "ConnectionStrings__$key=Server=localhost:${CLOUD_DB_LOCAL_PORT:-5440};User Id=${CLOUD_DB_USER:-postgres};Password=$CLOUD_DB_PASSWORD;Database=$db;Keepalive=15;Tcp Keepalive=true;Minimum Pool Size=3;Connection Idle Lifetime=300;Timeout=30;Command Timeout=60"
}

# Environment variables for one service: its database (cloud) and, for Search, the full re-sync.
svc_env() {
  db_env "$1"
  [ "$1" = search ] && [ "$RESYNC" = 1 ] && echo "Search__FullResync=true"
  return 0
}

echo "== 1/3 Docker: ${CONTAINERS// /, }   (database: $DB_TARGET)"
if ! docker info > /dev/null 2>&1; then
  echo "Docker is not running. Start Docker Desktop, wait until it says 'Engine running', then run this again."
  exit 1
fi
# shellcheck disable=SC2086
docker compose up -d $CONTAINERS || exit 1

if [ "$DB_TARGET" = cloud ]; then
  ./db-tunnel.sh status > /dev/null 2>&1 || ./db-tunnel.sh start || exit 1
fi

wanted=$(echo $CONTAINERS | wc -w)
printf "Waiting for the containers to be healthy"
for _ in $(seq 1 60); do
  # shellcheck disable=SC2086
  healthy=$(docker compose ps $CONTAINERS 2>/dev/null | grep -c "(healthy)")
  [ "$healthy" -ge "$wanted" ] && break
  printf "."; sleep 2
done
echo
if [ "${healthy:-0}" -lt "$wanted" ]; then
  echo "Containers are not healthy yet. Check: docker compose ps"
  exit 1
fi

running=0
for s in "${SERVICES[@]}"; do
  IFS="|" read -r name dir port <<< "$s"
  port_in_use "$port" && running=$((running + 1))
done
if [ "$running" -gt 0 ]; then
  echo "Some services are already running ($running of ${#SERVICES[@]})."
  echo "Run ./stop-backend.sh --keep-docker first, then start again."
  exit 1
fi

echo "== 2/3 Building the solution (first time takes a few minutes)"
if ! dotnet build Carsties.sln -v q -nologo > "$LOG_DIR/build.log" 2>&1; then
  echo "Build failed - see $LOG_DIR/build.log"
  exit 1
fi

echo "== 3/3 Starting services"
for s in "${SERVICES[@]}"; do
  IFS="|" read -r name dir port <<< "$s"
  mapfile -t svcenv < <(svc_env "$name")
  # Emptied first: the checks below must not read the previous run's "Now listening on" / "re-synced".
  : > "$LOG_DIR/$name.log"
  # Output through rotate-log.sh: each log file stays under 20 MB (3 files kept).
  (cd "src/$dir" && env "${svcenv[@]}" nohup bash -c 'dotnet run --no-build 2>&1 | "$0" "$1"' "$ROTATE" "$LOG_DIR/$name.log" > /dev/null 2>&1 &)
  printf "  %-9s http://localhost:%s " "$name" "$port"
  ok=0
  for _ in $(seq 1 90); do
    if grep -q "Now listening on" "$LOG_DIR/$name.log" 2>/dev/null; then ok=1; break; fi
    if grep -qE "Unhandled exception|Application startup exception" "$LOG_DIR/$name.log" 2>/dev/null; then break; fi
    printf "."; sleep 2
  done
  if [ "$ok" -eq 1 ]; then
    echo " ready"
  else
    echo " FAILED - see $LOG_DIR/$name.log"
    exit 1
  fi
done

if [ "$RESYNC" = 1 ]; then
  printf "  Search index: re-syncing from the %s database (last start: %s) " "$DB_TARGET" "${LAST_TARGET:-unknown}"
  synced=0
  for _ in $(seq 1 120); do
    if grep -q "Search index fully re-synced" "$LOG_DIR/search.log" 2>/dev/null; then synced=1; break; fi
    if grep -q "Failed to seed search" "$LOG_DIR/search.log" 2>/dev/null; then break; fi
    printf "."; sleep 2
  done
  if [ "$synced" = 1 ]; then
    echo " done ($(grep -o "fully re-synced from the AuctionService: .*" "$LOG_DIR/search.log" | tail -1 | cut -d: -f2- | sed 's/^ //'))"
    echo "$DB_TARGET" > "$RUN_DIR/db-target"
  else
    # Not remembered: the next start re-syncs again.
    echo " NOT finished - see $LOG_DIR/search.log (run again with RESYNC_SEARCH=1)"
  fi
fi

echo
echo "Backend is running (database: $DB_TARGET). Logs: $LOG_DIR"
echo "Test-mode OTP codes also appear in: $LOG_DIR/identity.log"
echo "Stop with: ./stop-backend.sh"
