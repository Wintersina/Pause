#!/bin/sh
# Rasterises every sample SVG in this folder with resvg (brew install resvg).
# Sprites render at 3x (128u -> 384 px), 64u pickups at 4x, worlds and UI at 2x.
# Output goes to ../frames/ ; compose.py turns those into strips, GIFs and sheets.
#
#   python3 build.py && ./render.sh && python3 compose.py
set -e
cd "$(dirname "$0")"
OUT=../frames
FONT=../../../Pause/Assets/Art/Orbitron/Orbitron-Bold.ttf
mkdir -p "$OUT"
for svg in *.svg; do
    name="${svg%.svg}"
    case "$name" in
        pickup_*) zoom=4 ;;
        world_*|ui_*) zoom=2 ;;
        *) zoom=3 ;;
    esac
    resvg --zoom "$zoom" --use-font-file "$FONT" "$svg" "$OUT/$name.png"
done
echo "rendered $(ls "$OUT" | wc -l | tr -d ' ') PNGs into $OUT"
