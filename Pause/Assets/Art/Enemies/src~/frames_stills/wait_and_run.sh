#!/bin/bash
# sleep until the Codex image cap resets (epoch 1792190536 + 360 s), then run the resumable queue
while [ "$(date +%s)" -lt 1792190896 ]; do sleep 30; done
/Users/sina/Developer/Pause/.claude/worktrees/agent-a689364e73d079013/Pause/Assets/Art/Enemies/src~/frames_stills/queue.sh
echo "queue finished $(date)" > /Users/sina/Developer/Pause/.claude/worktrees/agent-a689364e73d079013/Pause/Assets/Art/Enemies/src~/frames_stills/QUEUE_EXITED
