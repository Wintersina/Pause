#!/bin/sh
# Rebuilds Ninja from src~/pixel/approved.png (hull, damage rows, skins, data, rotors), then the bakes, then the test-rule check.
T="$(cd "$(dirname "$0")/../../../src~" && pwd)"
python3 "$T/pixel_restyle.py" Ninja && python3 "$T/bake_silhouettes.py" Ninja && python3 "$T/pixel_check.py" Ninja
