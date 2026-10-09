#!/usr/bin/env bash
# Starts the whole backend for local testing (Windows Git Bash):
#   1. Docker: postgres, rabbitmq, meilisearch (data stays in the Docker volumes)
#   2. Builds the solution once
#   3. Starts each .NET service in the background, in the right order, and waits until it answers
# Logs: bmwapi/.run/logs/<service>.log   Stop with: ./stop-backend.sh  (keeps all data)
set -uo pipefail

cd "$(dirname "$0")"
RUN_DIR="$(pwd)/.run"
LOG_DIR="$RUN_DIR/logs"
mkdir -p "$LOG_DIR"

# name | folder under src/ | port
SERVICES=(
  "identity|IdentityService|5001"
  "auction|AuctionService|7001"
  "search|SearchService|7002"
  "bid|BidService|7003"
  "notify|NotificationService|7004"
  "gateway|GatewayService|6001"
)

port_in_use() { netstat -ano | grep -E "TCP +(127\.0\.0\.1|0\.0\.0\.0|\[::1?\]):$1 .*LISTENING" > /dev/null; }

echo "== 1/3 Docker: postgres, rabbitmq, meilisearch"
if ! docker info > /dev/null 2>&1; then
  echo "Docker is not running. Start Docker Desktop, wait until it says 'Engine running', then run this again."
  exit 1
fi
docker compose up -d postgres rabbitmq meilisearch || exit 1

printf "Waiting for the containers to be healthy"
for _ in $(seq 1 60); do
  healthy=$(docker compose ps postgres rabbitmq meilisearch 2>/dev/null | grep -c "(healthy)")
  [ "$healthy" -ge 3 ] && break
  printf "."; sleep 2
done
echo
if [ "${healthy:-0}" -lt 3 ]; then
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
  (cd "src/$dir" && nohup dotnet run --no-build > "$LOG_DIR/$name.log" 2>&1 &)
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

echo
echo "Backend is running. Logs: $LOG_DIR"
echo "Test-mode OTP codes also appear in: $LOG_DIR/identity.log"
echo "Stop with: ./stop-backend.sh"
