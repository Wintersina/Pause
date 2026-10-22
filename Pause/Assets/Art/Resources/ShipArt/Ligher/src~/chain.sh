#!/bin/zsh
# Staged Ligher Codex runs. usage: chain.sh <stage> [reset_epoch]
#   j1d   -> wreck key pose (key_state2.png), then build, then STOP (check the wreck on dark)
#   j1b   -> pose sheets with modest banks (hull_redo_state*.png), then build, then STOP (check the banks)
#   j2    -> flames (flame_cruise/boost.png), then build, then STOP (needs damage_zones authored first, so skin refs are right)
#   skins -> Night, Amber, Bone, Flint one after another
# Each stage waits for the Codex image cap reset (epoch + 12 min) if it is not yet past, runs under the per-ship lock,
# and stops at the first job that did not write its output (usage limit?). Progress: codex/logs/chain.log.
W=/Users/sina/Developer/Pause/.claude/worktrees/agent-aa7f3d662ca2ad32f
S=$W/Pause/Assets/Art/Resources/ShipArt/Ligher/src~
C=$S/codex
B=$W/Pause/Assets/Art/Resources/ShipArt/src~/build_restyle.py
mkdir -p $C/logs
LOG=$C/logs/chain.log
say() { echo "$(date '+%F %T') $*" >> $LOG; }
STAGE=$1
T=${2:-1792685598}
say "stage $STAGE: waiting for epoch $T + 720"
while [ $(date +%s) -lt $((T + 720)) ]; do sleep 60; done

job() {  # job <brief> <expected output> <refs...>
  b=$1; out=$2; shift 2
  say "run $b"
  touch $C/logs/stamp
  sleep 1
  $S/run_codex.sh $b "$@"
  if [ ! "$C/$out" -nt $C/logs/stamp ]; then
    say "STOP: $b did not write $out (usage limit? see logs/log_$b.txt)"; exit 1
  fi
  say "done $b"
}
build() { python3 $B Ligher --no-skins --no-flame >> $LOG 2>&1; }

case $STAGE in
  j1d)   job j1d key_state2.png key_state0.png approved_key_state1.png mild_key_state2.png paragon_damaged.png; build ;;
  j1b)   job j1b hull_redo_state2.png key_state0.png key_state1.png key_state2.png bank_ref_neoncomet.png; build ;;
  j2)    job j2 flame_boost.png paragon_ship_alone.png key_state0.png ref_old_flame.png; build ;;
  skins) for n in Night Amber Bone Flint; do
           job skin_$n skin_${n}_state2.png skinref_state0.png skinref_state1.png skinref_state2.png
         done ;;
  *) say "unknown stage $STAGE"; exit 2 ;;
esac
say "stage $STAGE complete, stopping"
