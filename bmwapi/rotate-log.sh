#!/usr/bin/env bash
# Copies stdin to a log file and keeps it small: at MAX MB the file becomes <file>.1 (the older <file>.1 becomes
# <file>.2 …) and a new one starts. FILES files are kept in all (the current one + FILES-1 older ones).
#   some-command 2>&1 | ./rotate-log.sh <file> [max MB, default 20] [files kept, default 3]
# Used by start-backend.sh and db-tunnel.sh for everything in .run/logs.
set -uo pipefail

FILE="$1"; MAX_MB="${2:-20}"; FILES="${3:-3}"
# Byte counts (C locale); every line is flushed so "grep … <file>" sees it at once.
exec env LC_ALL=C awk -v f="$FILE" -v max="$((MAX_MB * 1024 * 1024))" -v files="$FILES" '
function rotate(   i) {
  close(f)
  for (i = files - 2; i >= 1; i--) system("mv -f \"" f "." i "\" \"" f "." (i + 1) "\" 2>/dev/null")
  if (files > 1) system("mv -f \"" f "\" \"" f ".1\" 2>/dev/null")
  n = 0
}
BEGIN { n = 0; printf "" > f }
{ print > f; fflush(f); n += length($0) + 1; if (n >= max) rotate() }'
