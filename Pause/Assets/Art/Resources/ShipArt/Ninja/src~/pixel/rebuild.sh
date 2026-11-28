#!/bin/sh
# Rebuilds Ninja from src~/pixel/approved.png: turn it 45 deg to a "+" (rotsprite.py), then hull, damage rows, skins, data,
# then the bakes, then the test-rule check.
D="$(cd "$(dirname "$0")" && pwd)"
T="$(cd "$D/../../../src~" && pwd)"
python3 "$T/rotsprite.py" "$D/approved.png" "$D/approved_plus.png" 45 160 1 && \
python3 "$T/pixel_restyle.py" Ninja && python3 "$T/bake_silhouettes.py" Ninja && python3 "$T/pixel_check.py" Ninja
