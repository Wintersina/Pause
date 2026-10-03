#!/bin/sh
# Rasterises the space dock's SVG sources into the PNG sprites the game loads.
#
#   brew install resvg
#   ./render.sh
#
# The SVGs are authored at 100 units = 1 world unit. They are rendered at 3x,
# so every PNG is 300 px per world unit -- DockArt.PixelsPerUnit must match.
# This folder ends in "~", so Unity never imports the sources themselves.
set -e
cd "$(dirname "$0")"
OUT=../Resources/Dock
ZOOM=3
mkdir -p "$OUT"
for svg in *.svg; do
    name="${svg%.svg}"
    resvg --zoom "$ZOOM" "$svg" "$OUT/$name.png"
    echo "rendered $OUT/$name.png"
done
