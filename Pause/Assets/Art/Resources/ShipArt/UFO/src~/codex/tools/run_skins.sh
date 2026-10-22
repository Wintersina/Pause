#!/bin/zsh
# Runs the four skin jobs one after another (each once, no retries).
T=/Users/sina/Developer/Pause/.claude/worktrees/agent-ae47bc826c0c4d00b/Pause/Assets/Art/Resources/ShipArt/UFO/src~/codex/tools
for S in Night Sodium Bone Abductor; do
  $T/run_job.sh skin_$S 0 skinref_state0.png skinref_state1.png skinref_state2.png
done
