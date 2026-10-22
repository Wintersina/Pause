#!/bin/zsh
# Waits for the Codex image cap reset (epoch $1, + 12 min), then runs the Ligher Codex jobs one at a time:
#   j1d (wreck) -> build -> j1b (poses, modest banks) -> build -> j2 (flames) -> build -> 4 skins
# Stops at the first job whose outputs did not get (re)written. Progress: codex/logs/chain.log.
# After it ends: review the frames, author damage_zones in restyle.json, final build/patch/bake (see the handoff).
W=/Users/sina/Developer/Pause/.claude/worktrees/agent-aa7f3d662ca2ad32f
S=$W/Pause/Assets/Art/Resources/ShipArt/Ligher/src~
C=$S/codex
B=$W/Pause/Assets/Art/Resources/ShipArt/src~/build_restyle.py
mkdir -p $C/logs
LOG=$C/logs/chain.log
say() { echo "$(date '+%F %T') $*" >> $LOG; }
T=${1:-1792685598}
say "chain started, waiting for epoch $T + 720"
while [ $(date +%s) -lt $((T + 720)) ]; do sleep 60; done
say "reset passed, starting"

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

job j1d key_state2.png key_state0.png approved_key_state1.png mild_key_state2.png paragon_damaged.png
build
job j1b hull_redo_state2.png key_state0.png key_state1.png key_state2.png bank_ref_neoncomet.png
build
job j2 flame_boost.png paragon_ship_alone.png key_state0.png ref_old_flame.png
build
for n in Night Amber Bone Flint; do
  job skin_$n skin_${n}_state2.png skinref_state0.png skinref_state1.png skinref_state2.png
done
say "chain complete"
