#!/usr/bin/env bash
# Stops heavy work (builds, tests, the emulator) when the disk is nearly full.
#   ./check-disk.sh [minimum free GB, default 3]    exit 1 (with the free space) when below
# Called by start-backend.sh and the e2e scripts before they start anything heavy.
set -uo pipefail

MIN_GB="${1:-${MIN_FREE_GB:-3}}"
dir="$(cd "$(dirname "$0")" && pwd)"
free_kb=$(df -Pk "$dir" | awk 'NR == 2 { print $4 }')
free_gb=$(awk -v k="$free_kb" 'BEGIN { printf "%.1f", k / 1024 / 1024 }')
if awk -v k="$free_kb" -v min="$MIN_GB" 'BEGIN { exit !(k < min * 1024 * 1024) }'; then
  echo "STOPPED: only $free_gb GB free on $(df -P "$dir" | awk 'NR == 2 { print $6 }') (needs at least $MIN_GB GB)."
  echo "Free some space first (e.g. Docker Desktop → Troubleshoot → Clean up, the Android emulator's snapshots, old builds), then run this again."
  exit 1
fi
echo "Disk: $free_gb GB free"
