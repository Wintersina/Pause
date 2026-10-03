#!/bin/sh
# Re-renders the Codex UI sprites (and Scripts/Codex/CodexPalette.cs) from the
# parametric sources in make_art.py. This folder ends in "~" so Unity ignores
# it. Requires python3 and resvg (brew install resvg).
set -e
cd "$(dirname "$0")"
python3 make_art.py
