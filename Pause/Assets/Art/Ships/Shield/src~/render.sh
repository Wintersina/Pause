#!/bin/sh
# Rasterizes the contour-shield art into the textures ShipShield loads from
# Resources/Shield. This folder ends in "~" so Unity ignores it (no SVG
# import of the sources). Requires resvg (brew install resvg).
#
#   ./render.sh     shield_atlas  (256x64)  plate / cracked / white / hot cells
#                   shield_impact (512x128) 4-frame hit flipbook
#                   shield_spark  (512x128) 4-frame activation anticipation
#                   shield_shards (256x64)  4 shatter shard shapes
set -e
cd "$(dirname "$0")"
out="../../../Resources/Shield"
mkdir -p "$out"
for name in shield_atlas shield_impact shield_spark shield_shards; do
  resvg "$name.svg" "$out/$name.png"
done
