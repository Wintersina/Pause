#!/bin/sh
# Re-renders the Flight Complete panel sprites from these SVG sources.
# The SVGs are authored in UI canvas units and rendered at 2x; the PNG
# importers use spritePixelsToUnits 200 so one canvas unit maps to two texels.
# This folder ends in "~" so Unity ignores it (no import of the sources).
# Needs resvg (brew install resvg).
set -e
here="$(cd "$(dirname "$0")" && pwd)"
out="$here/../../../Resources/DeathPanel"
mkdir -p "$out"
for name in dp_panel dp_card dp_bar dp_button dp_glow dp_divider dp_sparkle dp_pill dp_slab; do
  resvg --zoom 2 "$here/$name.svg" "$out/$name.png"
done
