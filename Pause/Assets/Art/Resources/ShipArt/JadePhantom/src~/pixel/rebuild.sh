#!/bin/sh
# Rebuilds JadePhantom from src~/pixel/approved.png (hull, damage rows, skins, data, rotors), then the bakes, then the test-rule check.
T="$(cd "$(dirname "$0")/../../../src~" && pwd)"
python3 "$T/pixel_restyle.py" JadePhantom && python3 "$T/bake_silhouettes.py" JadePhantom && python3 "$T/pixel_check.py" JadePhantom
