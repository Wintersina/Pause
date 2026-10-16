#!/usr/bin/env python3
"""Install the colourful flying roster: strips/ -> Resources/Enemies, ../desert_death -> Resources/Enemies/Death.
Run from anywhere:  python3 deploy_color.py [--sheet out.png]"""
import shutil, sys
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
ART = HERE.parents[2]                      # .../Assets/Art
KEYS = ["alien", "chaser", "big", "fighter_1", "fighter_2", "fighter_3", "fighter_4"]
for k in KEYS:
    n = f"desert_{k}.png"
    shutil.copy(HERE / "strips" / n, ART / "Resources/Enemies" / n)
    shutil.copy(HERE.parent / "desert_death" / n, ART / "Resources/Enemies/Death" / n)
print("installed", len(KEYS))
if "--sheet" in sys.argv:
    rows = [Image.open(ART / "Resources/Enemies/Death" / f"desert_{k}.png") for k in KEYS]
    sh = Image.new("RGBA", (max(r.width for r in rows), sum(r.height for r in rows)), (190, 165, 120, 255))
    y = 0
    for r in rows:
        sh.alpha_composite(r, (0, y)); y += r.height
    sh.save(sys.argv[sys.argv.index("--sheet") + 1])
