#!/bin/sh
# Rasterizes every ship's ultimate-weapon frames into the atlases Unity loads.
# This folder ends in "~" so Unity ignores it (no SVG import of the sources).
# Requires resvg (brew install resvg) and python3 with Pillow.
#
#   ./render.sh                 all ships -> ../../Resources/Weapons/<Ship>.png
#                               (and ../<Ship>/src~/*.svg, the per-frame sources)
#   ./render.sh --preview DIR   also writes a preview strip per ship into DIR
#   ./render.sh --only Ninja,UFO
#
# Colours live in the PALETTES table at the top of weapons.py; a restyle is an
# edit there followed by a re-render.
set -e
cd "$(dirname "$0")"
exec python3 weapons.py "$@"
