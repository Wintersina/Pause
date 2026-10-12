#!/bin/bash
# marketing-capture.sh -- runs the Unity Recorder marketing capture (Pause/Assets/Editor/Marketing).
# Opens the editor WINDOWED (the Recorder needs rendering; not -batchmode), plays through the menus and
# the four launch worlds, writes screenshots + MP4s to $MC_OUT and exits.
#
#   scripts/marketing-capture.sh                      # everything
#   MC_WORLDS=0,2 MC_JOBS=0 scripts/marketing-capture.sh
#   MC_OUT=/some/dir MC_PLAY_SECONDS=10 MC_BOSS_SECONDS=10 scripts/marketing-capture.sh   # quick test
#
# The developer-mode switch rewrites PlayerPrefs, so the editor's prefs file is backed up first and
# put back afterwards. Do not run while another Unity instance has this project open.
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$HERE/../Pause"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity}"
PREFS="$HOME/Library/Preferences/unity.WinterPeace.Pause.plist"
BACKUP="$(mktemp -d)/prefs.plist"
LOG="${MC_LOG:-/tmp/marketing-capture.log}"
export MC_EXIT=1
[ -f "$PREFS" ] && cp "$PREFS" "$BACKUP"
pkill -f Unity.Licensing.Client 2>/dev/null
"$UNITY" -projectPath "$PROJECT" -executeMethod MarketingCapture.CaptureAll -logFile "$LOG" -screen-fullscreen 0 &
UPID=$!
# an editor in the background stops ticking (nothing advances): keep it frontmost while it works
while kill -0 $UPID 2>/dev/null; do
  osascript -e "tell application \"System Events\" to set frontmost of (first process whose unix id is $UPID) to true" >/dev/null 2>&1
  sleep 15
done
wait $UPID
STATUS=$?
if [ -f "$BACKUP" ]; then defaults delete unity.WinterPeace.Pause >/dev/null 2>&1; cp "$BACKUP" "$PREFS"; killall cfprefsd 2>/dev/null; fi
echo "[marketing-capture] unity exit $STATUS, log $LOG"
exit $STATUS
