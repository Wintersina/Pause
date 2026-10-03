#!/bin/sh
# Rasterizes the four end-of-level bosses into the atlases BossArt.cs loads
# from Resources/Bosses. This folder ends in "~" so Unity ignores it (no SVG
# import of the sources). Requires resvg (brew install resvg) and python3
# with Pillow.
#
#   ./render.sh                 all bosses -> ../../Resources/Bosses/<World>.png,
#                               <World>_shots.png, <World>_card.png, warning.png
#                               (and ../<World>/src~/*.svg, the per-frame sources)
#   ./render.sh --preview DIR   also writes a review sheet + GIF per boss into DIR
#   ./render.sh --only Space,Ember
#
# The bosses are drawn in bosses.py (one function per world, plus the frame
# table); a change is an edit there followed by a re-render.
set -e
cd "$(dirname "$0")"
exec python3 bosses.py "$@"
