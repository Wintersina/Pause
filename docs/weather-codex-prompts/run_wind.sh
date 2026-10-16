#!/bin/bash
# Runs the per-world wind (wx_gust.png) Codex jobs, ONE codex exec at a time under the shared lock.
# usage: run_wind.sh <worktree> <codex_lock.sh> <scratch dir> [worlds...]   (default: Frost Verdant Ember Tide Desert Storm)
wt="$1"; lock="$2"; sc="$3"; shift 3; mkdir -p "$sc"
worlds=("$@"); [ ${#worlds[@]} -eq 0 ] && worlds=(Frost Verdant Ember Tide Desert Storm)
details() {
  case "$1" in
    Frost) echo "<WORLD>=Frost  palette: icy white #F0FAFF, pale blue #9FD8FF, deep blue-black shade; ribbons of blown snow and spindrift; heads: a rolling wall of white snow; debris: a snowflake and an ice shard. No pink, no red.";;
    Verdant) echo "<WORLD>=Verdant  palette: pollen yellow-white #F4FFD0, leaf green #7BD66B, deep green shade; ribbons of drifting pollen and spores; heads: a rolling cloud of pollen; debris: a curled leaf and a seed puff. No pink, no red.";;
    Ember) echo "<WORLD>=Ember  palette: amber #FFB347, hot gold #FFD98A, ash grey, deep charcoal; ribbons of rising heat-shimmer, ash and embers (they may swirl upward); heads: a billow of ash and sparks; debris: a glowing ember and an ash flake. No pink, no red.";;
    Tide) echo "<WORLD>=Tide  palette: sea-foam white #F0FFFA, mint aqua #7CF2C0, pale teal #8FD0C4, deep teal shade; ribbons of surf current: streaming water lines with foam; heads: a curling wave crest; debris: a spray droplet cluster and a foam fleck. No pink, no red.";;
    Desert) echo "<WORLD>=Desert  palette: sun-gold sand #F3D9A0, ochre #D8B070, dusty brown shade; ribbons of blown sand and grit in streaming eddies; heads: a rolling sand wall; debris: a grit cluster and a small dried tumbleweed twig. No pink, no red.";;
    Storm) echo "<WORLD>=Storm  palette: storm white-violet #E6E2FF, lavender #B8A8FF, slate blue-black; ribbons of charged rain and wind, a few faint crackling filaments; heads: a rolling squall front; debris: a rain streak cluster and a small spark. No pink, no red.";;
  esac
}
for w in "${worlds[@]}"; do
  out="./Pause/Assets/Art/Weather~/$w/gust"
  { cat "$wt/docs/weather-codex-prompts/wind_gust.md"; echo; echo "<OUT>=$out"; echo "<WORLD DETAILS>"; details "$w"; } > "$sc/gust_$w.md"
  "$lock" ~/.local/bin/codex exec --skip-git-repo-check -C "$wt" -s workspace-write -c model_reasoning_effort='"high"' -o "$sc/gust_${w}_last.txt" - < "$sc/gust_$w.md" > "$sc/gust_$w.log" 2>&1
  echo "[$(date +%T)] done $w (exit $?)" >> "$sc/progress.txt"
done
