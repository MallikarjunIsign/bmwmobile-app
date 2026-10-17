#!/usr/bin/env bash
# Nightly backup of the ShopCarParts databases (cron, 03:15 UTC). One folder per run:
#   ~/shopcarparts-db/backups/<UTC date-time>/<db>.dump   (pg_dump custom format, for pg_restore)
#   ~/shopcarparts-db/backups/<UTC date-time>/globals.sql (roles, without passwords)
# Keeps 7 days. Log: ~/shopcarparts-db/backups/backup.log
set -euo pipefail

DBS="identity auctions bids chat"
CONTAINER=shopcarparts-postgres
ROOT="$HOME/shopcarparts-db/backups"
DIR="$ROOT/$(date -u +%Y-%m-%d_%H%M)"
umask 077
mkdir -p "$DIR"

docker exec "$CONTAINER" pg_dumpall -U postgres --globals-only --no-role-passwords > "$DIR/globals.sql"
for db in $DBS; do
  docker exec "$CONTAINER" pg_dump -U postgres -Fc "$db" > "$DIR/$db.dump.part"
  mv "$DIR/$db.dump.part" "$DIR/$db.dump"
done

# Older than 7 days -> delete (only our dated folders)
find "$ROOT" -mindepth 1 -maxdepth 1 -type d -name '20*' -mtime +6 -exec rm -rf {} +

echo "$(date -u +%FT%TZ) ok $DIR $(du -sh "$DIR" | cut -f1)"
