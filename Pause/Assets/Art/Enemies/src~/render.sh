#!/bin/sh
# Rasterises the enemy flipbooks into the strips Unity loads
# (Art/Resources/Enemies/<key>.png, frames butted left to right, no gap).
# This folder ends in "~" so Unity ignores it (no SVG import of the sources).
#
# The sources in svg/ are templates: every colour is written @NAME@ and
# filled in from palette.env here, so a restyle is a palette edit and a
# re-render. 128 u frames render at 1.5x (192 px); EnemyArt sets the sprite
# pixels-per-unit from each roster entry's world size.
# Requires resvg (brew install resvg) and python3 with Pillow.
#
#   python3 build.py && ./render.sh            every enemy
#   ./render.sh frost_                         only keys starting with frost_
set -e
cd "$(dirname "$0")"
out="../../Resources/Enemies"
mkdir -p "$out"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

subst="$tmp/palette.sed"
grep -E '^[A-Z_]+=#[0-9A-Fa-f]{6}$' palette.env | sed -E 's/^([A-Z_]+)=(.*)$/s|@\1@|\2|g/' > "$subst"

for src in svg/${1}*.svg; do
  name="$(basename "${src%.svg}")"
  sed -f "$subst" "$src" > "$tmp/$name.svg"
  if grep -q '@[A-Z_]*@' "$tmp/$name.svg"; then
    echo "render.sh: $src uses a colour missing from palette.env" >&2
    exit 1
  fi
  resvg --zoom 1.5 "$tmp/$name.svg" "$tmp/$name.png" &
  # keep a handful of resvg processes in flight
  while [ "$(jobs -r | wc -l)" -ge 8 ]; do sleep 0.05; done
done
wait
python3 strip.py "$tmp" "$out" "$1"

# The rail mine also keeps its legacy atlas (Art/Resources/Vfx/
# rail_bomb_themes_atlas.png: row = world, columns = dormant, lit, arming,
# burst; 313 px cells, read by RailBombSprites) and the two Ember beat frames
# (rail_mine_ember_1/2, 256 px), so everything that still shows the old art
# shows the restyled mine.
if [ -z "$1" ] || [ "${1#*mine}" != "$1" ] || [ "$1" = "space_" ] || [ "$1" = "ember_" ]; then
  vfx="../../Resources/Vfx"
  for w in space frost verdant ember; do
    for k in 0 1 4 5; do
      [ -f "$tmp/${w}_mine_$k.svg" ] && resvg -w 313 -h 313 "$tmp/${w}_mine_$k.svg" "$tmp/atlas_${w}_$k.png"
    done
  done
  [ -f "$tmp/atlas_space_0.png" ] && python3 atlas.py "$tmp" "$vfx/rail_bomb_themes_atlas.png"
  if [ -f "$tmp/ember_mine_0.svg" ]; then
    resvg -w 256 -h 256 "$tmp/ember_mine_0.svg" "$vfx/rail_mine_ember_1.png"
    resvg -w 256 -h 256 "$tmp/ember_mine_5.svg" "$vfx/rail_mine_ember_2.png"
  fi
fi
