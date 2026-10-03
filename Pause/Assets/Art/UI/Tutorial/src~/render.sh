#!/bin/sh
# Rasterizes the tutorial robot and speech-bubble sources into the sprites
# Unity imports (Art/Resources/Tutorial, loaded by RobotSpeaker and friends).
# This folder ends in "~" so Unity ignores it (no SVG import of the sources).
# Sources are drawn in UI canvas units and rendered at 2x (sprite ppu 200).
# Requires resvg (brew install resvg).
set -e
cd "$(dirname "$0")"
for name in bubble tail robot eye bar glow ring arrow; do
  resvg --zoom 2 "tut_$name.svg" "../../../Resources/Tutorial/tut_$name.png"
done
