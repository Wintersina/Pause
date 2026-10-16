#!/usr/bin/env python3
"""Death strips (3 x 384 px: flash, rupture, dispersal) for the five Tide elites from each hull's hover cell 3, with the HD recipe
of Art/Enemies/Elite/src~/build_death_strips_hd.py in Tide's cue colours (aqua-white core, no pink). Writes next to the live strips.
  python3 death.py [--dir <strip dir>]   default: Resources/Elites/Tide, copied to Enemies/Elite/Tide"""
import sys
from pathlib import Path
from PIL import Image
R = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(R / "Enemies/Elite/src~"))
import build_death_strips_hd as D
D.PINK = (150, 245, 225)                      # the core accent: Tide aqua-white, never pink outside Space
D.WORLDS["tide"] = ("Tide", (170, 245, 235), (90, 200, 235), [(255, 255, 255), (130, 225, 235), (255, 196, 90)], "plasma")
S = 384
args = sys.argv[1:]
src = Path(args[args.index("--dir") + 1]) if "--dir" in args else R / "Resources/Elites/Tide"
for k in "riptide bathyscaphe trawler thunderfin pearl_warden".split():
    key = f"tide_elite_{k}"
    base = Image.open(src / f"{key}.png").convert("RGBA").crop((3 * S, 0, 4 * S, S))
    strip = D.build_one(key, base, "tide", S)
    strip.save(src / f"{key}_death.png")
    print(key, strip.size)
