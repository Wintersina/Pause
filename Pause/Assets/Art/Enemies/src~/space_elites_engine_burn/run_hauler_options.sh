#!/bin/bash
# Waits for Codex's usage limit to reset, then renders two Singularity Hauler redesign options (masters/singularity_hauler_a.png / _b.png).
# Usage: run_hauler_options.sh <repo worktree> <scratch dir>
R="$1"; S="$2"
D="$R/Pause/Assets/Art/Enemies/src~/space_elites_engine_burn"
E="$R/Pause/Assets/Art/Enemies/src~/space_elites_restyle/original"
cat "$D/prompts/common.md" "$D/prompts/singularity_hauler.md" > "$S/prompt_hauler_base.md"
for opt in a b; do
  if [ "$opt" = a ]; then
    extra="Option A: a manta / teardrop tug with the core cradle at the nose, pincers curving forward."
  else
    extra="Option B: a wider, heavier crab-like tug: the core in a thick ring-shaped cradle between two big curved claw arms, a short wedge hull behind with 4 engine bells."
  fi
  out="Pause/Assets/Art/Enemies/src~/space_elites_engine_burn/masters/singularity_hauler_$opt.png"
  { cat "$S/prompt_hauler_base.md"; echo "$extra"; echo "Save as: $out"; } > "$S/prompt_hauler_$opt.md"
  until [ -f "$R/$out" ]; do
    codex exec -C "$R" -s workspace-write -c model_reasoning_effort='"high"' -i "$E/space_elite_singularity_hauler.png" \
      -o "$S/codex_hauler_${opt}_last.txt" - < "$S/prompt_hauler_$opt.md" > "$S/codex_hauler_$opt.log" 2>&1
    [ -f "$R/$out" ] || sleep 600
  done
done
echo done > "$S/hauler_options_done"
