#!/bin/zsh
# resumable: skips ships that already have <key>_flying.png; retries every 10 min while Codex's usage limit blocks it
cd /Users/sina/Developer/Pause/.claude/worktrees/agent-a328c04e1cd1a9657/Pause/Assets/Art/Enemies/src~/desert_enemies/flying_src
for k in desert_alien desert_chaser desert_big desert_fighter_1 desert_fighter_2 desert_fighter_3 desert_fighter_4; do
  tries=0
  while [ ! -f /Users/sina/Developer/Pause/.claude/worktrees/agent-a328c04e1cd1a9657/Pause/Assets/Art/Enemies/src~/desert_enemies/flying_src/${k}_flying.png ] && [ $tries -lt 40 ]; do
    /private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/codex_lock.sh codex exec -s workspace-write -C /Users/sina/Developer/Pause/.claude/worktrees/agent-a328c04e1cd1a9657/Pause/Assets/Art/Enemies/src~/desert_enemies/flying_src -i /Users/sina/Developer/Pause/.claude/worktrees/agent-a328c04e1cd1a9657/Pause/Assets/Art/Enemies/src~/desert_enemies/flying_src/${k}_in.png - < /Users/sina/Developer/Pause/.claude/worktrees/agent-a328c04e1cd1a9657/Pause/Assets/Art/Enemies/src~/desert_enemies/flying_src/$k.prompt > /Users/sina/Developer/Pause/.claude/worktrees/agent-a328c04e1cd1a9657/Pause/Assets/Art/Enemies/src~/desert_enemies/flying_src/$k.log 2>&1
    [ -f /Users/sina/Developer/Pause/.claude/worktrees/agent-a328c04e1cd1a9657/Pause/Assets/Art/Enemies/src~/desert_enemies/flying_src/${k}_flying.png ] || sleep 600
    tries=$((tries+1))
  done
done
echo done > /Users/sina/Developer/Pause/.claude/worktrees/agent-a328c04e1cd1a9657/Pause/Assets/Art/Enemies/src~/desert_enemies/flying_src/ALL_DONE
