#!/bin/sh
# Rasterizes the quick-action icon sources into the sprites Unity imports.
# This folder ends in "~" so Unity ignores it (no SVG import of the sources).
# Requires resvg (brew install resvg). The SVGs come from build_icons.py.
#
#   ./render.sh          full tiles (plate + rim + glyph) -> QuickAction_<name>.png
#   ./render.sh --glyph  glyph only (no plate)              -> QuickAction_<name>_glyph.png
#                        used inside framed buttons such as the Flight Complete panel,
#                        where a plate would read as a box inside a box.
#   ./render.sh --shine  idle shimmer flipbook: a hard BONE glint band sweeping
#                        across the plate -> Shine/QuickAction_<name>_shine_<k>.png
#                        (a subfolder, so nothing that loads QuickActions/ sees it)
set -e
cd "$(dirname "$0")"
out="../../../Resources/QuickActions"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
if [ "$1" = "--glyph" ]; then
  # play has no quick action; its glyph is the tutorial end card's PLAY.
  for name in replay home play; do
    sed -e '/class="plate"/d' -e '/<!--SHINE-->/d' -e 's/viewBox="0 0 128 128"/viewBox="10 10 108 108"/' \
      "icon_$name.svg" > "$tmp/icon_$name.svg"
    resvg -w 256 -h 256 "$tmp/icon_$name.svg" "$out/QuickAction_${name}_glyph.png"
  done
  exit 0
fi
if [ "$1" = "--shine" ]; then
  mkdir -p "$out/Shine"
  clip='<clipPath id="pc"><polygon points="32,14 96,14 114,32 114,96 96,114 32,114 14,96 14,32"/></clipPath>'
  for name in replay home; do
    k=0
    for x in -10 26 62 98; do
      band="<g clip-path=\"url(#pc)\"><polygon points=\"$x,128 $((x + 14)),128 $((x + 54)),0 $((x + 40)),0\" fill=\"#F4EAD4\" fill-opacity=\"0.55\"/><polygon points=\"$((x + 20)),128 $((x + 24)),128 $((x + 64)),0 $((x + 60)),0\" fill=\"#F4EAD4\" fill-opacity=\"0.55\"/></g>"
      sed -e "s|<!--SHINE-->|$clip$band|" "icon_$name.svg" > "$tmp/icon_$name.svg"
      resvg -w 256 -h 256 "$tmp/icon_$name.svg" "$out/Shine/QuickAction_${name}_shine_$k.png"
      k=$((k + 1))
    done
  done
  exit 0
fi
for name in replay home; do
  resvg -w 256 -h 256 "icon_$name.svg" "$out/QuickAction_$name.png"
done
