#!/bin/sh
# Rebuilds VoltViper from src~/pixel/approved.png (hull, damage rows, skins, data, rotors), then the bakes, then the test-rule check.
T="$(cd "$(dirname "$0")/../../../src~" && pwd)"
python3 "$T/pixel_restyle.py" VoltViper && python3 "$T/bake_silhouettes.py" VoltViper && python3 "$T/pixel_check.py" VoltViper
