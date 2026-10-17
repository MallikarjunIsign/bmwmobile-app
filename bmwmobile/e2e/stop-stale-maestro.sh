#!/usr/bin/env bash
# Stops Maestro processes left over from an interrupted UI test. They keep Maestro's session file locked,
# and the next run then waits forever ("Failed to record heartbeat ... locked a portion of the file").
powershell -NoProfile -Command '
  Get-CimInstance Win32_Process |
    Where-Object { $_.Name -eq "java.exe" -and $_.CommandLine -match "maestro" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force; "  stopped a leftover Maestro (PID $($_.ProcessId))" }
' 2>/dev/null
exit 0
