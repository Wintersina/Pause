#!/bin/zsh
# Dove chain 3: the four skin jobs, one at a time (refs = skinref_state0/1/2.png written by build_restyle.py).
WT=/Users/sina/Developer/Pause/.claude/worktrees/agent-ac66a148d5d2f4a3c
C=$WT/Pause/Assets/Art/Resources/ShipArt/Dove/src~/codex
export CODEX_LOCK=/tmp/pause-codex-dove.lock.d
for s in Night Sodium Bone Courier; do
  for try in 1 2 3; do
    echo "== skin $s try $try $(date)"
    touch $C/.marker_$s
    $WT/scripts/codex-art.sh $WT $C/briefs/skin_$s.md $C/last_skin_$s.txt $C/skinref_state0.png $C/skinref_state1.png $C/skinref_state2.png > $C/log_skin_$s.log 2>&1
    if [ -f $C/skin_${s}_state2.png ] && [ $C/skin_${s}_state2.png -nt $C/.marker_$s ]; then echo "$s done"; break; fi
    tail -5 $C/log_skin_$s.log; sleep 1200
  done
done
echo "CHAIN3 DONE $(date)"
