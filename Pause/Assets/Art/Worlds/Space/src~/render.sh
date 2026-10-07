#!/bin/sh
# Re-renders the Space background from its SVG templates.
# Requires resvg (brew install resvg) and Python 3 with Pillow.
#
#   ./render.sh   -> svg/*.svg sources (frame 00 of each flipbook) and
#                    Backgrounds/Resources/Worlds/Space/Backdrop/{sky,far,mid,flow,fx,anim}.png + atlas .json
#
# Palette: ../../src~/palette.py. Templates: ../../src~/space.py.
set -e
cd "$(dirname "$0")/../../src~"
rm -rf "../Space/src~/svg"
python3 space.py
