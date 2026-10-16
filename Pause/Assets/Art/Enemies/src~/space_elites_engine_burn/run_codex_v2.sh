#!/bin/bash
# v2 (hard-surface, grounded, referenced on Steel Hound + Static / Mirror Bastion). Retries every 10 min while Codex's usage limit is hit.
# Usage: run_codex_v2.sh <repo worktree> <scratch dir>
R="$1"; S="$2"
D="$R/Pause/Assets/Art/Enemies/src~/space_elites_engine_burn"
REL="Pause/Assets/Art/Enemies/src~/space_elites_engine_burn/masters/v2"
mkdir -p "$D/masters/v2"
job() {  # name brief out [extra]
  local name="$1" brief="$2" out="$3" extra="$4"
  { cat "$D/prompts/common.md" "$D/prompts/$brief.md"; [ -n "$extra" ] && echo "$extra"; echo "Save as: $REL/$out.png"; } > "$S/v2_$out.md"
  until [ -f "$R/$REL/$out.png" ]; do
    /private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/codex_lock.sh codex exec -C "$R" -s workspace-write -c model_reasoning_effort='"high"' \
      -i "$D/refs/steel_hound.png" -i "$D/refs/static_bastion.png" -i "$D/refs/mirror_bastion.png" \
      -o "$S/v2_${out}_last.txt" - < "$S/v2_$out.md" > "$S/v2_$out.log" 2>&1
    [ -f "$R/$REL/$out.png" ] || sleep 600
  done
}
job x singularity_hauler hauler_a "Option A: the collar and core at the front half of the hull, the two grapple arms reaching forward and outward past it."
job x singularity_hauler hauler_b "Option B: a broader, flatter tug: the collar centred, the grapple arms folded along both flanks of the hull, a wide engine deck."
job x rift_lancer rift_lancer
job x eventide_bastion eventide_bastion
job x orbit_reaver orbit_reaver
echo done > "$S/v2_done"
