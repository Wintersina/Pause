#!/bin/zsh
# Dove chain 2 (key_state0 approved): J1c (redo damage keys) -> J1b (intact pose sheet) -> J2 (flames), one at a time.
WT=/Users/sina/Developer/Pause/.claude/worktrees/agent-ac66a148d5d2f4a3c
C=$WT/Pause/Assets/Art/Resources/ShipArt/Dove/src~/codex
export CODEX_LOCK=/tmp/pause-codex-dove.lock.d
run() {  # name brief outfile(newer-than marker) refs...
  local name=$1 brief=$2 outf=$3; shift 3
  for try in 1 2 3; do
    echo "== $name try $try $(date)"
    touch $C/.marker_$name
    $WT/scripts/codex-art.sh $WT $brief $C/last_$name.txt "$@" > $C/log_$name.txt 2>&1
    if [ -f $outf ] && [ $outf -nt $C/.marker_$name ]; then echo "$name done"; return 0; fi
    echo "$name produced no fresh $outf; tail:"; tail -5 $C/log_$name.txt
    sleep 1200
  done
  return 1
}
run j1c $C/briefs/j1c.md $C/key_state2.png $C/key_state0.png $C/paragon_damaged.png $C/key_state1_REJECTED.png $C/key_state2_REJECTED.png || exit 1
run j1b $C/briefs/j1b.md $C/hull_redo_state0.png $C/key_state0.png $C/ref_neoncomet_banks.png || exit 2
run j2 $C/briefs/j2.md $C/flame_boost.png $C/paragon_ship_alone.png $C/key_state0.png $C/ref_old_flame_cruise.png $C/ref_old_flame_boost.png || exit 3
echo "CHAIN2 DONE $(date)"
