#!/bin/sh
# Re-renders the Verdant background from its SVG templates.
# Requires resvg (brew install resvg) and Python 3 with Pillow.
#
#   ./render.sh   -> svg/*.svg sources (frame 00 of each flipbook) and
#                    Backgrounds/Resources/Worlds/Verdant/Backdrop/{sky,far,mid,flow,fx,anim}.png + atlas .json
#
# Palette: ../../src~/palette.py. Templates: ../../src~/verdant.py.
set -e
cd "$(dirname "$0")/../../src~"
rm -rf "../Verdant/src~/svg"
python3 verdant.py
