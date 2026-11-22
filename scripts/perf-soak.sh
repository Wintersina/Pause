#!/bin/bash
# perf-soak.sh -- Play-mode performance soak (Pause/Assets/Editor/Tools/PerfSoak.cs): flies real runs with
# the autopilot in a batch editor (through the unity-batch queue) and prints the "[PERFSOAK]" lines.
#
#   scripts/perf-soak.sh                                   # Volt Viper in Space, 240 simulated seconds
#   PS_RUNS=2:0,7:6,13:0 PS_SECONDS=300 scripts/perf-soak.sh   # ship:world pairs (ids: shopingShips.Roster, WorldManager.Worlds)
#
# Developer mode rewrites PlayerPrefs, so the editor's prefs are exported first and imported back afterwards.
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
DOMAIN=unity.WinterPeace.Pause
BACKUP="$(mktemp -d)/prefs.plist"
defaults export "$DOMAIN" "$BACKUP" 2>/dev/null || BACKUP=""
LOG="${PS_LOG:-/tmp/pause-unity-logs/perf-soak-$(date +%Y%m%d-%H%M%S)-$$.log}"
mkdir -p "$(dirname "$LOG")"
UNITY_BATCH_NO_QUIT=1 "$HERE/unity-batch.sh" -projectPath "$HERE/../Pause" -executeMethod PerfSoak.Run -logFile "$LOG"
STATUS=$?
if [ -n "$BACKUP" ] && [ -f "$BACKUP" ]; then defaults delete "$DOMAIN" >/dev/null 2>&1; defaults import "$DOMAIN" "$BACKUP"; fi
grep -a "\[PERFSOAK\]" "$LOG" | grep -av "^UnityEngine\|^  at "
echo "[perf-soak] unity exit $STATUS, log $LOG"
exit $STATUS
