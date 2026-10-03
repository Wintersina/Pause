#!/bin/sh
# Rasterizes the tutorial robot, speech bubble and tutorial UI sources into
# the sprites Unity imports (Art/Resources/Tutorial).
# This folder ends in "~" so Unity ignores it (no SVG import of the sources).
#
# The sources are templates: every colour is written @NAME@ and filled in
# from palette.env here, so a restyle is a palette edit and a re-render.
# Drawn in UI canvas units and rendered at 2x (sprite ppu 200).
# Requires resvg (brew install resvg).
set -e
cd "$(dirname "$0")"
out="../../../Resources/Tutorial"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# palette.env -> one sed expression per colour.
subst="$tmp/palette.sed"
grep -E '^[A-Z_]+=#[0-9A-Fa-f]{6}$' palette.env | sed -E 's/^([A-Z_]+)=(.*)$/s|@\1@|\2|g/' > "$subst"

for src in tut_*.svg; do
  name="${src%.svg}"
  sed -f "$subst" "$src" > "$tmp/$src"
  if grep -q '@[A-Z_]*@' "$tmp/$src"; then
    echo "render.sh: $src uses a colour missing from palette.env" >&2
    exit 1
  fi
  resvg --zoom 2 "$tmp/$src" "$out/$name.png"
done
