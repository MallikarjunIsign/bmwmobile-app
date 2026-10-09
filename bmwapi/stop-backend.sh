#!/usr/bin/env bash
# Stops the local backend started with ./start-backend.sh (or by hand with dotnet run).
#   ./stop-backend.sh               stop the .NET services and the Docker containers
#   ./stop-backend.sh --keep-docker stop only the .NET services
# Data is always kept: containers are stopped, never removed, and volumes are never deleted.
set -uo pipefail

cd "$(dirname "$0")"

PORTS="5001 7001 7002 7003 7004 6001"

echo "== Stopping .NET services"
for port in $PORTS; do
  pids=$(netstat -ano | grep -E "TCP +(127\.0\.0\.1|0\.0\.0\.0|\[::1?\]):$port .*LISTENING" | awk '{print $NF}' | sort -u)
  if [ -z "$pids" ]; then
    echo "  port $port: not running"
    continue
  fi
  for pid in $pids; do
    taskkill //PID "$pid" //T //F > /dev/null 2>&1 && echo "  port $port: stopped (PID $pid)"
  done
done

if [ "${1:-}" = "--keep-docker" ]; then
  echo "Docker containers left running."
  exit 0
fi

echo "== Stopping Docker containers (data volumes are kept)"
if docker info > /dev/null 2>&1; then
  docker compose stop postgres rabbitmq meilisearch
else
  echo "  Docker is not running - nothing to stop."
fi
echo "Done."
