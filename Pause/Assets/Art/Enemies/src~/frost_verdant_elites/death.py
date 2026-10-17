#!/usr/bin/env python3
"""Death strips (3 x 384 px: flash, rupture, dispersal) for the rebuilt Cryo Siren, Rimebreaker and Resin Warden from each hull's FINAL hover
cell 3 (the HD recipe of Art/Enemies/Elite/src~/build_death_strips_hd.py in the world's cue colours: no pink outside Space).
  python3 death.py [--dir <strip dir>] [ship ...]      default: Resources/Elites/<World>"""
import sys
from pathlib import Path
from PIL import Image
HERE = Path(__file__).resolve().parent
ART = HERE.parents[2]
sys.path.insert(0, str(ART / "Enemies/Elite/src~"))
sys.path.insert(0, str(HERE))
import build_death_strips_hd as D
import build_strips as B
S = 384
D.WORLDS["frost"] = ("Frost", (200, 245, 255), (110, 190, 255), [(255, 255, 255), (150, 215, 255), (210, 240, 255)], "ice")
D.WORLDS["verdant"] = ("Verdant", (255, 226, 140), (255, 168, 60), [(255, 252, 235), (255, 205, 100), (230, 150, 50)], "leaf")
ACCENT = {"frost": (190, 235, 255), "verdant": (255, 214, 110)}   # the core disc: never pink
args = sys.argv[1:]
src = None
if "--dir" in args:
    i = args.index("--dir"); src = Path(args[i + 1]); del args[i:i + 2]
for name in (args or list(B.SHIPS)):
    cfg = B.SHIPS[name]
    world = cfg["world"].lower()
    d = src or ART / "Resources/Elites" / cfg["world"]
    if not (d / f"{cfg['key']}.png").exists():
        continue
    D.PINK = ACCENT[world]
    base = Image.open(d / f"{cfg['key']}.png").convert("RGBA").crop((3 * S, 0, 4 * S, S))
    strip = D.build_one(cfg["key"], base, world, S)
    strip.save(d / f"{cfg['key']}_death.png")
    print(cfg["key"], strip.size)
