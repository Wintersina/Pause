#!/usr/bin/env python3
"""Reference images for the Dove restyle: old cells (4x nearest on green) from the LEGACY sheet.
Only valid before the restyle build overwrites Dove/Dove.png (take the old sheet from master: git show origin/master:<path>).
The old flame refs (ref_old_flame_*.png) were cut from the legacy flame data once and are committed."""
import os
import sys
from PIL import Image

CODEX = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OLD = sys.argv[1] if len(sys.argv) > 1 else os.path.join(CODEX, "..", "..", "Dove.png")
sheet = Image.open(OLD).convert("RGBA")
for st in range(3):
    cell = sheet.crop((0, st * 256, 256, (st + 1) * 256)).resize((1024, 1024), Image.NEAREST)
    bg = Image.new("RGBA", (1024, 1024), (0, 255, 0, 255))
    bg.alpha_composite(cell)
    bg.convert("RGB").save(os.path.join(CODEX, "ref_old_state%d.png" % st))
print("ok")
