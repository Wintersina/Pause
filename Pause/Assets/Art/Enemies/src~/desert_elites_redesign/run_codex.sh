#!/bin/bash
# Usage: run_codex.sh <repo worktree> <scratch dir> <name...>
# One image per codex exec, always under the shared lock; retries every 10 min while the usage limit blocks it.
R="$1"; S="$2"; shift 2
D="$R/Pause/Assets/Art/Enemies/src~/desert_elites_redesign"
REL="Pause/Assets/Art/Enemies/src~/desert_elites_redesign/masters"
for name in "$@"; do
  cat "$D/prompts/common.md" "$D/prompts/$name.md" > "$S/p_$name.md"
  tries=0
  until [ -f "$R/$REL/$name.png" ] || [ $tries -ge 40 ]; do
    /private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/codex_lock.sh codex exec -C "$R" -s workspace-write -c model_reasoning_effort='"high"' -o "$S/last_$name.txt" - < "$S/p_$name.md" > "$S/log_$name.txt" 2>&1
    if [ ! -f "$R/$REL/$name.png" ]; then grep -i "usage limit\|try again" "$S/log_$name.txt" | tail -2 > "$S/limit_$name.txt"; sleep 600; fi
    tries=$((tries+1))
  done
done
echo done > "$S/codex_done"
