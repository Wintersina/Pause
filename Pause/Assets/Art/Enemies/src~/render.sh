#!/bin/sh
# Rasterises the enemy flipbooks into the strips Unity loads
# (Art/Resources/Enemies/<key>.png, frames butted left to right, no gap).
# This folder ends in "~" so Unity ignores it (no SVG import of the sources).
#
# The sources in svg/ are templates: every colour is written @NAME@ and
# filled in from palette.env here, so a restyle is a palette edit and a
# re-render. 128 u frames render at 1.5x (192 px; the heavies at 2x, 256 px);
# EnemyArt sets the sprite
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
  # The heavies show at ~1.1 world units (EnemyRoster.BigWidth), about twice
  # the rest, so they rasterise at 2x (256 px frames) to keep their detail.
  zoom=1.5
  case "$name" in *_big_*) zoom=2 ;; esac
  resvg --zoom "$zoom" "$tmp/$name.svg" "$tmp/$name.png" &
  # keep a handful of resvg processes in flight
  while [ "$(jobs -r | wc -l)" -ge 8 ]; do sleep 0.05; done
done
wait
python3 strip.py "$tmp" "$out" "$1"

# The rail mines are not rendered here any more: they play the original neon
# pixel-art atlas (Art/Resources/Enemies/Mines/rail_mines_neon.png, sliced by
# RailMineArt), which is kept byte-for-byte as the user approved it.
