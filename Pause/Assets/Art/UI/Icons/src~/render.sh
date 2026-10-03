#!/bin/sh
# Rasterizes the quick-action icon sources into the sprites Unity imports.
# This folder ends in "~" so Unity ignores it (no SVG import of the sources).
# Requires resvg (brew install resvg).
#
#   ./render.sh          full tiles (plate + rim + glyph) -> QuickAction_<name>.png
#   ./render.sh --glyph  glyph only (no plate, no rim)     -> QuickAction_<name>_glyph.png
#                        used inside framed buttons such as the Flight Complete panel,
#                        where a plate would read as a box inside a box.
set -e
cd "$(dirname "$0")"
out="../../../Resources/QuickActions"
if [ "$1" = "--glyph" ]; then
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' EXIT
  for name in replay home; do
    # The plate and rim are the only <rect> elements in each source; the
    # viewBox is cropped to the glyph (plus its glow) so it fills the sprite.
    sed -e '/<rect /d' -e 's/viewBox="0 0 128 128"/viewBox="18 18 92 92"/' \
      "icon_$name.svg" > "$tmp/icon_$name.svg"
    resvg -w 256 -h 256 "$tmp/icon_$name.svg" "$out/QuickAction_${name}_glyph.png"
  done
  exit 0
fi
for name in replay home; do
  resvg -w 256 -h 256 "icon_$name.svg" "$out/QuickAction_$name.png"
done
