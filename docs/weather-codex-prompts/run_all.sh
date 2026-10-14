#!/bin/bash
# Runs the weather Codex jobs one after another in this worktree (max 2 codex exec machine-wide: waits while 2 run).
# usage: run_all.sh <worktree> <scratch dir> [jobs...]   (jobs default: SpaceSun Storm Desert Frost Verdant Ember Tide Space)
wt="$1"; sc="$2"; shift 2; mkdir -p "$sc"
jobs=("$@"); [ ${#jobs[@]} -eq 0 ] && jobs=(SpaceSun Storm Desert Frost Verdant Ember Tide Space)
for j in "${jobs[@]}"; do
  while [ "$(pgrep -f 'codex exec' | wc -l | tr -d ' ')" -ge 2 ]; do sleep 30; done
  if [ "$j" = SpaceSun ]; then cat "$wt/docs/weather-codex-prompts/space_sun.md" > "$sc/$j.md"
  else cat "$wt/docs/weather-codex-prompts/common.md" "$wt/docs/weather-codex-prompts/worlds/$j.md" > "$sc/$j.md"; fi
  refs=(); 
  if [ "$j" != SpaceSun ]; then b="$wt/Pause/Assets/Art/Backgrounds/Resources/Worlds/$j/Backdrop3/v1"; [ -d "$b" ] && f=$(ls "$b"/mid*.png 2>/dev/null | head -1) && [ -n "$f" ] && refs=(-i "$f"); fi
  [ "$j" = SpaceSun ] && refs=(-i "$wt/Pause/Assets/Art/Backgrounds/Resources/Worlds/Space/Backdrop/reference_planet.png" -i "$wt/Pause/Assets/Art/Backgrounds/Resources/Worlds/Space/Backdrop/sky_01.png")
  echo "[$(date +%T)] start $j" >> "$sc/progress.txt"
  ~/.local/bin/codex exec --skip-git-repo-check -C "$wt" -s workspace-write -c model_reasoning_effort='"high"' "${refs[@]}" -o "$sc/${j}_last.txt" - < "$sc/$j.md" > "$sc/$j.log" 2>&1
  echo "[$(date +%T)] done $j (exit $?)" >> "$sc/progress.txt"
done
