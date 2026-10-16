#!/usr/bin/env python3
"""Death strips (3 x 384 px: flash, rupture, dispersal) for the six Desert elites, rebuilt from each NEW hull (bare cell 0) with the HD recipe
of Art/Enemies/Elite/src~/build_death_strips_hd.py, but in each ship's own palette (the recipe's hard-coded pink core is swapped for the
ship's warm sun-white; no pink outside Space). Writes Resources/Elites/Desert/<key>_death.png and the copy next to Codex's strips."""
import sys
from pathlib import Path
from PIL import Image
R = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(R / "Enemies/Elite/src~"))
import build_death_strips_hd as D
D.PINK = (255, 244, 214)   # the rupture core (was Space pink)
# key: (hot, mid, accents, shard style)
PAL = {
    "dune_stalker": ((190, 225, 255), (70, 120, 235), [(255, 255, 255), (150, 215, 255), (235, 200, 120)], "plasma"),
    "caravan_hauler": ((255, 225, 140), (255, 160, 50), [(255, 255, 255), (255, 215, 90), (90, 225, 215)], "slag"),
    "glass_cannon": ((255, 235, 160), (255, 190, 70), [(255, 255, 255), (255, 225, 120), (120, 215, 220)], "ice"),
    "lens_matriarch": ((190, 255, 225), (80, 215, 170), [(255, 255, 255), (150, 235, 255), (230, 200, 120)], "ice"),
    "mirror_bastion": ((200, 225, 255), (120, 130, 255), [(255, 255, 255), (150, 225, 255), (235, 200, 110)], "ice"),
    "scorpion_walker": ((255, 225, 140), (255, 150, 50), [(255, 255, 255), (255, 210, 90), (255, 170, 60)], "plasma"),
}
S = 384
for k, (hot, mid, acc, style) in PAL.items():
    key = f"desert_elite_{k}"
    D.WORLDS["d_" + k] = ("Desert", hot, mid, acc, style)
    base = Image.open(R / f"Resources/Elites/Desert/{key}.png").convert("RGBA").crop((0, 0, S, S))
    strip = D.build_one(key, base, "d_" + k, S)
    strip.save(R / f"Resources/Elites/Desert/{key}_death.png")
    strip.save(R / f"Enemies/Elite/Desert/{key}_death.png")
    print(key, strip.size)
