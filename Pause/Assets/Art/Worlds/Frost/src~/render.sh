#!/bin/sh
# Re-renders the Frost background from its SVG templates.
# Requires resvg (brew install resvg) and Python 3 with Pillow.
#
#   ./render.sh   -> svg/*.svg sources (frame 00 of each flipbook) and
#                    Backgrounds/Resources/Worlds/Frost/Backdrop/{sky,far,mid,flow,fx,anim}.png + atlas .json
#
# Palette: ../../src~/palette.py. Templates: ../../src~/frost.py.
set -e
cd "$(dirname "$0")/../../src~"
rm -rf "../Frost/src~/svg"
python3 frost.py
