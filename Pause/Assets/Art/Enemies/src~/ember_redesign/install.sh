#!/bin/bash
# Install the built redesign strips (run after vetting strips/ at 1x and 3x). Enemies/hazards -> Resources/Enemies (+ Death);
# elites -> Resources/Elites/Ember and Enemies/Elite/Ember (+ _death). Death strips come from build_ember_enemy_death.py.
# Elite points (nozzles / muzzles in Resources/Elites/Defs/ember_elite_*.json) must be re-measured afterwards (add-elite-ship skill)
# because the new hulls differ from the old ones.
set -e
cd "$(dirname "$0")/.."                      # Art/Enemies/src~
ART="$(cd ../.. && pwd)"
R="ember_redesign/strips"
EN=(); EL=()
for f in "$R"/ember_*.png; do n="$(basename "$f" .png)"; case "$n" in ember_elite_*) EL+=("$n");; *) EN+=("$n");; esac; done
for n in "${EN[@]}"; do cp "$R/$n.png" "$ART/Resources/Enemies/$n.png"; done
EMBER_IDLE="$ART/Resources/Enemies" python3 build_ember_enemy_death.py "${EN[@]}" >/dev/null
for n in "${EN[@]}"; do cp ember_refresh_death/$n.png "$ART/Resources/Enemies/Death/$n.png"; done
if [ ${#EL[@]} -gt 0 ]; then
  TMP="$(mktemp -d)"; for n in "${EL[@]}"; do cp "$R/$n.png" "$TMP/"; done
  EMBER_IDLE="$TMP" python3 build_ember_enemy_death.py "${EL[@]}" >/dev/null
  for n in "${EL[@]}"; do
    cp "$R/$n.png" "$ART/Resources/Elites/Ember/$n.png"; cp "$R/$n.png" "$ART/Enemies/Elite/Ember/$n.png"
    cp ember_refresh_death/$n.png "$ART/Resources/Elites/Ember/${n}_death.png"; cp ember_refresh_death/$n.png "$ART/Enemies/Elite/Ember/${n}_death.png"
  done
fi
echo installed
