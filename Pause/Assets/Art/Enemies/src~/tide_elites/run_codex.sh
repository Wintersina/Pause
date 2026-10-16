#!/bin/bash
# Codex master generation for the Tide elites (no reference images: designs are described in words). One image per call, shared lock.
# Usage: run_codex.sh <repo worktree> <scratch dir> [names...]
R="$1"; S="$2"; shift 2
D="$R/Pause/Assets/Art/Enemies/src~/tide_elites"
REL="Pause/Assets/Art/Enemies/src~/tide_elites/masters"
mkdir -p "$D/masters"
NAMES="${*:-riptide bathyscaphe trawler thunderfin pearl_warden}"
for n in $NAMES; do
  { cat "$D/prompts/common.md" "$D/prompts/$n.md"; echo "Save as: $REL/$n.png"; } > "$S/tide_$n.md"
  tries=0
  until [ -f "$R/$REL/$n.png" ] || [ $tries -ge 12 ]; do
    /private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/codex_lock.sh codex exec -C "$R" -s workspace-write -c model_reasoning_effort='"high"' \
      -o "$S/tide_${n}_last.txt" - < "$S/tide_$n.md" > "$S/tide_$n.log" 2>&1
    [ -f "$R/$REL/$n.png" ] || { tries=$((tries+1)); sleep 600; }
  done
  echo "$n done" >> "$S/tide_progress"
done
echo done > "$S/tide_done"
