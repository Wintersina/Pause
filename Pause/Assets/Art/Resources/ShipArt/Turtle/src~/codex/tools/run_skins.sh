#!/bin/zsh
# Runs the four Turtle skin jobs one after another (each through the Turtle-private lock; run_job.sh handles a
# confirmed usage_limit_reached once by waiting for resets_at). Stops after the last one.
C=/Users/sina/Developer/Pause/.claude/worktrees/agent-a9a7e5b36102d3a11/Pause/Assets/Art/Resources/ShipArt/Turtle/src~/codex
S=/private/tmp/claude-501/-Users-sina-Developer-Pause/f81f62e1-5015-4fec-8294-5f213a5c6d1f/scratchpad/turtle
for n in Shellback Night Sodium Bone; do
  $C/tools/run_job.sh 0 $C/briefs/skin_$n.md $S/skin_${n}_msg.txt $S/skin_$n.log $C/skinref_state0.png $C/skinref_state1.png $C/skinref_state2.png
  echo "skin $n finished" >> $S/skins_progress.txt
done
