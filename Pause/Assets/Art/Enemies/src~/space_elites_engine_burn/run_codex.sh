#!/bin/bash
# Runs Codex headlessly, one ship at a time, to redraw the four Space elites simpler (see prompts/).
# Usage: run_codex.sh <repo worktree> <scratch dir>
R="$1"; S="$2"
D="$R/Pause/Assets/Art/Enemies/src~/space_elites_engine_burn"
E="$R/Pause/Assets/Art/Enemies/src~/space_elites_restyle/original"
mkdir -p "$D/masters"
for s in rift_lancer eventide_bastion orbit_reaver singularity_hauler; do
  cat "$D/prompts/common.md" "$D/prompts/$s.md" > "$S/prompt_$s.md"
  codex exec -C "$R" -s workspace-write -c model_reasoning_effort='"high"' -i "$E/space_elite_$s.png" \
    -o "$S/codex_${s}_last.txt" - < "$S/prompt_$s.md" > "$S/codex_$s.log" 2>&1
done
echo done > "$S/codex_all_done"
