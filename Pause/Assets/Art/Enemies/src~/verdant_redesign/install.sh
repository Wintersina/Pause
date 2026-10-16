#!/bin/bash
# Install the built Verdant redesign (run after vetting strips/ at 1x and 3x): idle strips -> Resources/Enemies, death strips ->
# Resources/Enemies/Death (scripted from each new strip's idle cell 3 by build_death.py). verdant_alien (Snap Sprout, PR #25) is never touched.
set -e
cd "$(dirname "$0")"
ART="$(cd ../../.. && pwd)"          # Assets/Art
NAMES=("$@")
if [ ${#NAMES[@]} -eq 0 ]; then for f in strips/verdant_*.png; do NAMES+=("$(basename "$f" .png)"); done; fi
python3 build_death.py "${NAMES[@]}" >/dev/null
for n in "${NAMES[@]}"; do
  cp "strips/$n.png" "$ART/Resources/Enemies/$n.png"
  cp "death/$n.png" "$ART/Resources/Enemies/Death/$n.png"
done
echo installed ${#NAMES[@]}
