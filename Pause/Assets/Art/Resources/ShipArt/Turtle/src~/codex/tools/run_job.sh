#!/bin/zsh
# usage: run_job.sh <start-epoch> <brief> <last-msg> <log> <refs...>
# Sleeps until <start-epoch>, then runs one Codex job through scripts/codex-art.sh with a Turtle-private lock.
# If the log shows usage_limit_reached it reads resets_at, sleeps until then + 10 min and retries (max 4 tries).
WT=/Users/sina/Developer/Pause/.claude/worktrees/agent-a9a7e5b36102d3a11
START=$1; BRIEF=$2; MSG=$3; LOG=$4; shift 4
export CODEX_LOCK=/tmp/pause-codex-turtle.lock.d
now=$(date +%s)
if (( START > now )); then sleep $(( START - now )); fi
for try in 1 2 3 4; do
  $WT/scripts/codex-art.sh $WT $BRIEF $MSG "$@" > $LOG 2>&1
  if grep -q usage_limit_reached $LOG; then
    R=$(grep -o 'resets_at[^0-9]*[0-9]*' $LOG | grep -o '[0-9]*$' | tail -1)
    now=$(date +%s)
    if [[ -n "$R" ]] && (( R + 600 > now )); then sleep $(( R + 600 - now )); else sleep 900; fi
    continue
  fi
  break
done
echo "JOB DONE try=$try" >> $LOG
