#!/bin/sh
# Rasterizes the quick-action icon sources into the sprites Unity imports.
# This folder ends in "~" so Unity ignores it (no SVG import of the sources).
# Requires resvg (brew install resvg).
set -e
cd "$(dirname "$0")"
for name in replay home play; do
  resvg -w 256 -h 256 "icon_$name.svg" "../../../Resources/QuickActions/QuickAction_$name.png"
done
