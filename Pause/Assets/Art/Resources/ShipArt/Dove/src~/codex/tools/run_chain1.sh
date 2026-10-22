#!/bin/zsh
# Dove chain 1: wait for the Codex image cap reset, then J1 (key poses) -> J1b (poses) -> J2 (flames), one at a time.
# usage: run_chain1.sh [reset_epoch]   (default: Thu 2026-10-22 11:13 CDT + 10 min + random minutes)
WT=/Users/sina/Developer/Pause/.claude/worktrees/agent-ac66a148d5d2f4a3c
C=$WT/Pause/Assets/Art/Resources/ShipArt/Dove/src~/codex
export CODEX_LOCK=/tmp/pause-codex-dove.lock.d
RESET=${1:-1792685598}
TARGET=$(( RESET + 600 + RANDOM % 300 ))
echo "target epoch $TARGET, now $(date +%s)"
while [ $(date +%s) -lt $TARGET ]; do sleep 60; done
echo "starting at $(date)"

run() {  # name brief outfile refs...
  local name=$1 brief=$2 outf=$3; shift 3
  for try in 1 2 3; do
    echo "== $name try $try $(date)"
    $WT/scripts/codex-art.sh $WT $brief $C/last_$name.txt "$@" > $C/log_$name.txt 2>&1
    if [ -f $outf ]; then echo "$name done"; return 0; fi
    echo "$name produced no $outf; log tail:"; tail -5 $C/log_$name.txt
    sleep 1200
  done
  return 1
}

run j1 $C/briefs/j1.md $C/key_state2.png $C/paragon_ship_alone.png $C/paragon_damaged.png $C/ref_old_state0.png $C/ref_old_state1.png $C/ref_old_state2.png || exit 1
echo "CHAIN1 DONE (J1 only: key poses need a human look before J1b/J2) $(date)"
