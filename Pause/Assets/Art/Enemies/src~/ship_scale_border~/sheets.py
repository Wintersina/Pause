#!/usr/bin/env python3
"""Per-world before/after sheets of the outline pass: original | the ship's configured side (modes.json), at 1x (192 px cell) and at
the real on-screen size on a 1080-wide phone (7.44 u across = 145 px per world unit), mid-dark backdrop.
    python3 sheets.py <Resources dir> <measure.json> <out dir>"""
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import add_border as ab
CFG = json.load(open(HERE / "modes.json"))
RES, MEAS, OUT = Path(sys.argv[1]), json.load(open(sys.argv[2])), Path(sys.argv[3])
OUT.mkdir(parents=True, exist_ok=True)
PXU, BG = 1080 / 7.44, (64, 60, 104, 255)
WN = ["space", "frost", "verdant", "ember", "tide", "storm", "desert"]
SET = {  # representative ships per world (elite + enemies); singularity and glowjelly are the user's examples
    "space": ["space_elite_singularity_hauler", "space_elite_rift_lancer", "space_alien", "space_fighter_2"],
    "frost": ["frost_elite_cryo_siren", "frost_elite_whiteout_sentinel", "frost_alien", "frost_fighter_3"],
    "verdant": ["verdant_elite_resin_warden", "verdant_alien", "verdant_fighter_3", "verdant_rock_pod"],
    "ember": ["ember_elite_sunstoke", "ember_elite_coalrunner", "ember_fighter_1", "ember_alien"],
    "tide": ["tide_alien", "tide_elite_thunderfin", "tide_fighter_2", "tide_rock_urchin"],
    "storm": ["storm_elite_rod_dancer", "storm_alien", "storm_fighter_2", "storm_rock_hail"],
    "desert": ["desert_elite_lens_matriarch", "desert_alien", "desert_fighter_3", "desert_rock_dune"],
}
def cell(path, idx):
    im = Image.open(path).convert("RGBA"); return im.crop((idx * 192, 0, idx * 192 + 192, 192))
for world, keys in SET.items():
    tiles = []
    for key in keys:
        if "_elite_" in key:
            d = json.load(open(RES / "Elites/Defs" / (key + ".json")))
            idx = ((d.get("cells") or {}).get("flight") or [0])[0]
            src = RES / "Elites" / world.capitalize() / (key + ".png"); cw = d["cellWorldSize"]
            name, kind = d["displayName"], "elites"
        else:
            e = MEAS["enemies"][key]; idx = 0
            src = RES / "Enemies" / (key + ".png"); cw = e["cell_world"] * 1.2
            name, kind = e["name"], "enemies"
        orig = HERE / "original" / (key + ".png")
        arr = np.array(Image.open(orig if orig.exists() else src).convert("RGBA"))
        wd = 1 if kind == "elites" else 2
        mode = CFG["ships"].get(key, CFG["default"])
        ims = [Image.fromarray(arr).crop((idx * 192, 0, idx * 192 + 192, 192))]
        ims.append(Image.fromarray(ab.border(arr, wd, ab.TONE[world], 192, mode, CFG["fade"])).crop((idx * 192, 0, idx * 192 + 192, 192)))
        tiles.append(("%s -> %s" % (name, mode), kind, ims, max(96, int(round(cw * PXU)))))
    colw = [192, 192] + [max(t[3] for t in tiles)] * 2
    W = sum(colw) + 6 * 20
    H = 44 + sum(max(192, t[3]) + 30 for t in tiles)
    img = Image.new("RGBA", (W, H), BG); dr = ImageDraw.Draw(img)
    dr.text((16, 8), "%s: 1x  original | outlined (side per modes.json)      real size on a 1080-wide phone  original | outlined" % world.upper(), fill=(255, 255, 255, 255))
    y = 30
    for name, kind, ims, px in tiles:
        rh = max(192, px)
        dr.text((16, y), "%s [%s]" % (name, kind[:-1]), fill=(255, 240, 180, 255))
        x = 16
        for col in range(4):
            im = ims[col % 2] if col < 2 else ims[col - 2].resize((px, px), Image.LANCZOS)
            img.alpha_composite(im, (x, y + 16 + (rh - im.size[1]) // 2)); x += colw[col] + 20
        y += rh + 30
    p = OUT / ("outline_modes_%s.png" % world); img.convert("RGB").save(p, optimize=True); print(p, img.size)
