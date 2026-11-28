#!/bin/sh
# Rebuilds UFO from src~/pixel/approved.png (hull, damage rows, skins, data, rotors), then the bakes, then the test-rule check.
T="$(cd "$(dirname "$0")/../../../src~" && pwd)"
python3 "$T/pixel_restyle.py" UFO && python3 "$T/bake_silhouettes.py" UFO && python3 "$T/pixel_check.py" UFO
