#!/bin/zsh
# usage: run_job.sh <job name: j1|j1b|skin_Night|...> <not-before epoch> <ref images...>
# Waits until the epoch (Codex image cap reset + margin), then runs scripts/codex-art.sh with this ship's private lock.
W=/Users/sina/Developer/Pause/.claude/worktrees/agent-ae47bc826c0c4d00b
C=$W/Pause/Assets/Art/Resources/ShipArt/UFO/src~/codex
JOB=$1; AT=$2; shift 2
while [ $(date +%s) -lt $AT ]; do sleep 30; done
REFS=()
for r in "$@"; do REFS+=($C/$r); done
CODEX_LOCK=/tmp/pause-codex-ufo.lock.d $W/scripts/codex-art.sh $W $C/briefs/$JOB.md $C/last_$JOB.txt "${REFS[@]}" > $C/log_$JOB.txt 2>&1
echo "job $JOB exit $?" >> $C/log_$JOB.txt
