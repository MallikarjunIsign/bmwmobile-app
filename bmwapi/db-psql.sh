#!/usr/bin/env bash
# psql on the database the backend uses now: the local container, or the Oracle VM (over SSH) for DB_TARGET=cloud.
#   ./db-psql.sh <database> [psql options …]  < script.sql        e.g.  ./db-psql.sh identity -q -v email=x@y.test
# The target is DB_TARGET, else the last start (.run/db-target), else local. Used by the e2e scripts for test accounts.
set -uo pipefail
cd "$(dirname "$0")"

DB="$1"; shift
TARGET="${DB_TARGET:-$(cat .run/db-target 2>/dev/null | tr -d '\r\n')}"
case "${TARGET:-local}" in
  cloud)
    set -a; . .run/cloud-db.env; set +a
    # Options are passed through quoted (printf %q), so values with spaces survive the remote shell.
    exec ssh -i "${CLOUD_DB_SSH_KEY:-$HOME/.ssh/bidmyparts}" -o BatchMode=yes -o ConnectTimeout=15 \
      "$CLOUD_DB_SSH_USER@$CLOUD_DB_SSH_HOST" "docker exec -i shopcarparts-postgres psql -U postgres -d $(printf %q "$DB") $(printf '%q ' "$@")" ;;
  *)
    exec docker exec -i bmwapi-postgres-1 psql -U postgres -d "$DB" "$@" ;;
esac
