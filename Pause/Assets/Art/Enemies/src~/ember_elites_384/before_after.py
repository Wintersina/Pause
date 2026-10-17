#!/usr/bin/env python3
"""Before / after sheets for the PR: legacy 192 px flight cell (shown x2) over the new 384 px flight cell, plus a full new-strip sheet.
  python3 before_after.py <out dir>"""
import sys
from pathlib import Path
from PIL import Image
sys.path.insert(0, str(Path(__file__).parent))
import make_inputs as mi
R = Path(__file__).resolve().parents[3] / "Resources/Elites/Ember"
out = Path(sys.argv[1])
BG = (30, 24, 60, 255)
sh = Image.new("RGBA", (384 * 6, 384 * 2), BG)
for i, n in enumerate(mi.NAMES):
    old = mi.legacy(n).crop((576, 0, 768, 192)).resize((384, 384), Image.Resampling.NEAREST)
    new = Image.open(R / f"ember_elite_{n}.png").convert("RGBA").crop((3 * 384, 0, 4 * 384, 384))
    sh.alpha_composite(old, (i * 384, 0)); sh.alpha_composite(new, (i * 384, 384))
sh.save(out / "before_after_flight.png")
full = Image.new("RGBA", (384 * 8, 384 * 6), BG)
for i, n in enumerate(mi.NAMES):
    full.alpha_composite(Image.open(R / f"ember_elite_{n}.png").convert("RGBA"), (0, i * 384))
full.save(out / "new_strips_all_cells.png")
death = Image.new("RGBA", (384 * 3, 384 * 6), BG)
for i, n in enumerate(mi.NAMES):
    death.alpha_composite(Image.open(R / f"ember_elite_{n}_death.png").convert("RGBA"), (0, i * 384))
death.save(out / "new_death_strips.png")
