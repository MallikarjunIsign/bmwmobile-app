#!/usr/bin/env bash
# SSH tunnel to the cloud PostgreSQL on the Oracle VM (Windows Git Bash):
#   localhost:5440  ->  VM 127.0.0.1:5432   (the database port is never open to the internet)
#   ./db-tunnel.sh start    start in the background; reconnects by itself when the connection drops
#   ./db-tunnel.sh stop     stop it
#   ./db-tunnel.sh status   running or not
# Settings (host, user, key, local port, DB password): bmwapi/.run/cloud-db.env (git-ignored, see RUN_LOCAL.md).
# Log: bmwapi/.run/logs/db-tunnel.log
set -uo pipefail

cd "$(dirname "$0")"
RUN_DIR="$(pwd)/.run"
ENV_FILE="$RUN_DIR/cloud-db.env"
PID_FILE="$RUN_DIR/db-tunnel.pid"
LOG_FILE="$RUN_DIR/logs/db-tunnel.log"
mkdir -p "$RUN_DIR/logs"

if [ ! -f "$ENV_FILE" ]; then
  echo "Missing $ENV_FILE - see RUN_LOCAL.md (cloud database)."
  exit 1
fi
set -a; . "$ENV_FILE"; set +a
LOCAL_PORT="${CLOUD_DB_LOCAL_PORT:-5440}"
SSH_KEY="${CLOUD_DB_SSH_KEY:-$HOME/.ssh/bidmyparts}"

port_listening() { netstat -ano | grep -E "TCP +127\.0\.0\.1:$LOCAL_PORT .*LISTENING" > /dev/null; }
port_pids() { netstat -ano | grep -E "TCP +127\.0\.0\.1:$LOCAL_PORT .*LISTENING" | awk '{print $NF}' | sort -u; }
loop_running() { [ -f "$PID_FILE" ] && kill -0 "$(cat "$PID_FILE")" 2> /dev/null; }

case "${1:-}" in
  _loop)
    # Runs in the background until the PID file is removed by "stop".
    while [ -f "$PID_FILE" ]; do
      echo "$(date '+%F %T') connecting to $CLOUD_DB_SSH_USER@$CLOUD_DB_SSH_HOST"
      # Keep-alive every 10 s (SSH + TCP) so NAT / idle timeouts don't drop it; gone after ~60 s without an answer,
      # then this loop reconnects. The database connections themselves use Npgsql keepalive (start-backend.sh).
      ssh -N -i "$SSH_KEY" \
        -o BatchMode=yes -o ExitOnForwardFailure=yes \
        -o ServerAliveInterval=10 -o ServerAliveCountMax=6 -o TCPKeepAlive=yes -o ConnectTimeout=15 \
        -L "127.0.0.1:$LOCAL_PORT:127.0.0.1:5432" \
        "$CLOUD_DB_SSH_USER@$CLOUD_DB_SSH_HOST"
      echo "$(date '+%F %T') tunnel ended (exit $?)"
      [ -f "$PID_FILE" ] && sleep 5
    done
    ;;
  start)
    if loop_running; then
      echo "Tunnel already running (localhost:$LOCAL_PORT)."
      exit 0
    fi
    if port_listening; then
      echo "Port $LOCAL_PORT is already used by another program (PID $(port_pids)). Change CLOUD_DB_LOCAL_PORT in $ENV_FILE."
      exit 1
    fi
    # The log stays under 20 MB (3 files), like the services' logs.
    if [ -f "$LOG_FILE" ] && [ "$(wc -c < "$LOG_FILE")" -gt $((20 * 1024 * 1024)) ]; then
      mv -f "$LOG_FILE.1" "$LOG_FILE.2" 2> /dev/null; mv -f "$LOG_FILE" "$LOG_FILE.1"
    fi
    echo "started" > "$PID_FILE"
    nohup "$0" _loop >> "$LOG_FILE" 2>&1 &
    echo $! > "$PID_FILE"
    printf "Starting tunnel localhost:%s -> %s 127.0.0.1:5432 " "$LOCAL_PORT" "$CLOUD_DB_SSH_HOST"
    for _ in $(seq 1 20); do
      if port_listening; then echo " ready"; exit 0; fi
      printf "."; sleep 1
    done
    echo " FAILED - see $LOG_FILE"
    "$0" stop > /dev/null
    exit 1
    ;;
  stop)
    pid=""; [ -f "$PID_FILE" ] && pid=$(cat "$PID_FILE")
    rm -f "$PID_FILE"
    [ -n "$pid" ] && kill "$pid" 2> /dev/null
    for p in $(port_pids); do taskkill //PID "$p" //F > /dev/null 2>&1; done
    echo "Tunnel stopped."
    ;;
  status)
    if loop_running && port_listening; then
      echo "Tunnel running: localhost:$LOCAL_PORT -> $CLOUD_DB_SSH_HOST 127.0.0.1:5432"
    elif loop_running; then
      echo "Tunnel reconnecting (see $LOG_FILE)"; exit 1
    else
      echo "Tunnel not running."; exit 1
    fi
    ;;
  *)
    echo "Usage: $0 start|stop|status"
    exit 1
    ;;
esac
