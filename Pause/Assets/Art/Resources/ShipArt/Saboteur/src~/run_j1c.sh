#!/bin/zsh
WT=/Users/sina/Developer/Pause/.claude/worktrees/agent-a71e29e38407814db
CX=$WT/Pause/Assets/Art/Resources/ShipArt/Saboteur/src~/codex
export CODEX_LOCK=/tmp/pause-codex-saboteur.lock.d
$WT/scripts/codex-art.sh $WT $CX/briefs/j1c.md $CX/last_j1c.txt $CX/key_state0.png $CX/paragon_damaged.png $CX/v1/key_state2.png > $CX/log_j1c.txt 2>&1
echo "j1c rc=$?" >> /private/tmp/claude-501/-Users-sina-Developer-Pause/f81f62e1-5015-4fec-8294-5f213a5c6d1f/scratchpad/saboteur/status.txt
