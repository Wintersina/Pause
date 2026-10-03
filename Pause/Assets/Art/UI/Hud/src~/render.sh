#!/bin/sh
# Rasterises the HUD sprites (HudStyler) into Art/Resources/Hud.
# Drawn in UI canvas units, rendered at 2x (sprite ppu 200). Needs resvg.
# This folder ends in "~", so Unity never imports the sources themselves.
set -e
here="$(cd "$(dirname "$0")" && pwd)"
out="$here/../../../Resources/Hud"
mkdir -p "$out"
for name in hud_panel hud_meter; do
  resvg --zoom 2 "$here/$name.svg" "$out/$name.png"
done
