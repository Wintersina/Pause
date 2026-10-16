#!/usr/bin/env python3
"""Measure the hardpoints of the redesigned Desert elites on the built 384 px strips and write them into the def JSONs (cellPixels 384).
Muzzles are the real weapon hardpoints, read off the Codex master (master pixel coordinates below) and carried through the exact
transform build_strips.py used (scale + offset of the hull in its cell); nozzles are the engine-mouth centres the flames were drawn at,
snapped onto solid art. Also writes an overlay per ship (muzzles yellow, nozzles cyan) on the hover cell.
  python3 points.py <preview dir>"""
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, str(Path(__file__).parent))
import build_strips as bs

R = Path(__file__).resolve().parents[3]
DEF = R / "Resources/Elites/Defs"
STRIP = R / "Resources/Elites/Desert"
S = bs.CELL

# name -> [(muzzle name, master x, master y, dir)]   dir -1 = along the nose
MUZZLES = {
    "dune_stalker": [("hook", 512, 42, -1.0)],
    "caravan_hauler": [("port", 190, 520, -1.0), ("star", 834, 520, -1.0), ("canister", 512, 720, -1.0)],
    "glass_cannon": [("cannon", 627, 36, -1.0)],
    "lens_matriarch": [("fan", 512, 50, -1.0), ("portrod", 270, 430, -1.0), ("starrod", 754, 430, -1.0)],
    "mirror_bastion": [("core", 510, 100, -1.0), ("portcap", 320, 200, -1.0), ("starcap", 700, 200, -1.0)],
    "scorpion_walker": [("claw", 512, 50, -1.0)],
}


def solid_near(a, x, y, r):
    best = None
    for yy in range(max(0, y - r), min(S, y + r + 1)):
        for xx in range(max(0, x - r), min(S, x + r + 1)):
            if a[yy, xx] >= 200:
                dd = (xx - x) ** 2 + (yy - y) ** 2
                if best is None or dd < best[0]:
                    best = (dd, xx, yy)
    return best[1], best[2]


def main():
    out = Path(sys.argv[1]); out.mkdir(parents=True, exist_ok=True)
    for name, cfg in bs.SHIPS.items():
        master = bs.key_out(bs.HERE / "masters" / f"{cfg['src']}.png")
        bb = master.getchannel("A").point(lambda v: 255 if v > 128 else 0).getbbox()
        art, (ox, oy), noz = bs.layout(name)
        s = min(cfg["w"] / (bb[2] - bb[0]), cfg["h"] / (bb[3] - bb[1]))
        strip = Image.open(STRIP / f"{cfg['key']}.png").convert("RGBA")
        hov = strip.crop((3 * S, 0, 4 * S, S))
        a = np.array(strip.crop((0, 0, S, S)))[..., 3]   # cell 0: the bare hull
        muz = []
        for n, mx, my, d in MUZZLES[name]:
            x, y = ox + round((mx - bb[0]) * s), oy + round((my - bb[1]) * s)
            x, y = solid_near(a, x, y, 14)
            far = ((x - S / 2) ** 2 + (y - S / 2) ** 2) ** .5
            if far > 154:   # EliteTest wants every point within .42 of the cell from its centre: step back from the tip along the line to the centre
                k = 154 / far
                x, y = solid_near(a, round(S / 2 + (x - S / 2) * k), round(S / 2 + (y - S / 2) * k), 10)
            muz.append(dict(name=n, x=x, y=y, dir=d))
        nz = []
        for i, (x, y) in enumerate(noz):
            x, y = solid_near(a, x, y - 14, 24)
            nz.append(dict(name="abc"[i], x=x, y=y, dir=-1.0))
        p = DEF / f"{cfg['key']}.json"
        d = json.load(open(p))
        d["muzzles"], d["nozzles"], d["cellPixels"] = muz, nz, S
        open(p, "w").write(json.dumps(d, indent=2) + "\n")
        bg = Image.new("RGBA", (S, S), (205, 184, 138, 255)); bg.alpha_composite(hov)
        dr = ImageDraw.Draw(bg)
        for m in muz: dr.ellipse((m["x"] - 8, m["y"] - 8, m["x"] + 8, m["y"] + 8), outline=(255, 0, 255, 255), width=2)
        for n in nz: dr.ellipse((n["x"] - 8, n["y"] - 8, n["x"] + 8, n["y"] + 8), outline=(0, 0, 0, 255), width=2)
        bg.save(out / f"points_{name}.png")
        print(name, "muzzles", [(m["name"], m["x"], m["y"]) for m in muz], "nozzles", [(n["x"], n["y"]) for n in nz])


main()
