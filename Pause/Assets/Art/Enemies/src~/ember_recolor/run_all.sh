#!/bin/bash
# Ember colour pass, end to end (run from anywhere): recolour -> border (enemies) / engine glow (elites) -> death strips -> install.
set -e
cd "$(dirname "$0")/.."                       # Art/Enemies/src~
ART="$(cd ../.. && pwd)"
python3 ember_recolor/recolor.py >/dev/null
python3 ember_border/add_border.py >/dev/null  # writes Resources/Enemies/ember_*.png
python3 ember_recolor/fire.py >/dev/null            # flame tongues behind the bordered enemy hulls (in place) and at the elite nozzles
TMP="$(mktemp -d)"; cp ember_recolor/out/elites_glow/*.png "$TMP"/
EMBER_IDLE="$ART/Resources/Enemies" python3 build_ember_enemy_death.py ember_alien ember_chaser ember_big ember_fighter_1 ember_fighter_2 ember_fighter_3 ember_fighter_4 ember_rock_cinder ember_rock_islet ember_rock_magma ember_rock_obsidian >/dev/null
EMBER_IDLE="$TMP" python3 build_ember_enemy_death.py ember_elite_sunstoke ember_elite_coalrunner ember_elite_brass_vulture ember_elite_kilnback ember_elite_ash_wraith ember_elite_cauterizer >/dev/null
for k in alien chaser big fighter_1 fighter_2 fighter_3 fighter_4 rock_cinder rock_islet rock_magma rock_obsidian; do cp ember_refresh_death/ember_$k.png "$ART/Resources/Enemies/Death/"; done
for n in sunstoke coalrunner brass_vulture kilnback ash_wraith cauterizer; do k=ember_elite_$n
  cp ember_recolor/out/elites_glow/$k.png "$ART/Resources/Elites/Ember/$k.png"; cp ember_recolor/out/elites_glow/$k.png "$ART/Enemies/Elite/Ember/$k.png"
  cp ember_refresh_death/$k.png "$ART/Resources/Elites/Ember/${k}_death.png"; cp ember_refresh_death/$k.png "$ART/Enemies/Elite/Ember/${k}_death.png"; done
echo done
