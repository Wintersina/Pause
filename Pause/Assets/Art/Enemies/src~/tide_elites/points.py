#!/usr/bin/env python3
"""Measure each Tide elite's muzzles + nozzles on the built 384 px hover cell and write them into the def JSON (cellPixels 384).
Muzzle anchors are shares of the hull's alpha bbox read off the master (the lance tip, the claw charges, the boom spools, the rod tip, the coil
drums, the pearl aperture ...), snapped onto solid art; nozzles are the engine-bell mouths the flames are drawn at (build_strips.py).
Also: the shot colours become Tide's aqua-white cue (no pink outside Space) and engineColor follows each ship's flame; a preview per ship.
  python3 points.py <preview dir> [--no-write]"""
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, str(Path(__file__).parent))
import build_strips as bs

R = Path(__file__).resolve().parents[3]
DEF = R / "Resources/Elites/Defs"
STRIP = R / "Resources/Elites/Tide"
S = bs.CELL
# (name, share of bbox width, share of bbox height)
MUZZLES = {
    "riptide": [("lance", .5, .03)],
    "bathyscaphe": [("portclaw", .10, .74), ("starclaw", .90, .74), ("rack", .5, .84)],
    "trawler": [("port", .045, .38), ("star", .955, .38), ("winch", .5, .67)],
    "thunderfin": [("rod", .5, .02), ("coilp", .37, .48), ("coils", .63, .48)],
    "pearl_warden": [("shellp", .27, .58), ("shells", .73, .58)],
}
ENGINE = {"riptide": "#82E1FF", "bathyscaphe": "#9FF2FF", "trawler": "#96D7FF", "thunderfin": "#A8A0FF", "pearl_warden": "#8CFFE8"}
SHOT = "#7FF0FF"; CORE = "#F2FFFF"


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
    out = Path(sys.argv[1])
    write = "--no-write" not in sys.argv
    for name, cfg in bs.SHIPS.items():
        art = bs.hull(name)
        w, h = art.size
        ox, oy = (S - w) // 2, (S - h - bs.FLAME_ROOM + 20) // 2
        arr = np.array(Image.open(STRIP / f"tide_elite_{name}.png").convert("RGBA").crop((0, 0, S, S)))   # cell 0: the bare hull
        a = arr[..., 3]
        p = DEF / f"tide_elite_{name}.json"
        d = json.load(open(p))
        old = {m["name"]: m for m in d["muzzles"]}
        olds = list(d["muzzles"])
        muz = []
        for i, (nm, fx, fy) in enumerate(MUZZLES[name]):
            x, y = solid_near(a, ox + round(fx * w), oy + round(fy * h), 24)
            muz.append(dict(name=nm, x=x, y=y, dir=olds[i]["dir"] if i < len(olds) else -1.0))
        nz = []
        for i, (fx, fy) in enumerate(cfg["nz"]):
            x, y = solid_near(a, ox + round(fx * w), oy + round(fy * h) - 6, 20)
            nz.append(dict(name="lr"[i] if len(cfg["nz"]) == 2 else str(i), x=x, y=y, dir=-1.0))
        d["muzzles"], d["nozzles"], d["cellPixels"] = muz, nz, S
        d["engineColor"] = ENGINE[name]
        d["shotColor"], d["shotCore"] = SHOT, CORE
        if write:
            open(p, "w").write(json.dumps(d, indent=2) + "\n")
        im = Image.open(STRIP / f"tide_elite_{name}.png").convert("RGBA").crop((3 * S, 0, 4 * S, S))
        bg = Image.new("RGBA", (S, S), (16, 52, 84, 255)); bg.alpha_composite(im)
        dr = ImageDraw.Draw(bg)
        for m in muz: dr.ellipse((m["x"] - 8, m["y"] - 8, m["x"] + 8, m["y"] + 8), outline=(255, 255, 0, 255), width=2)
        for n in nz: dr.ellipse((n["x"] - 8, n["y"] - 8, n["x"] + 8, n["y"] + 8), outline=(0, 255, 255, 255), width=2)
        bg.save(out / f"points_{name}.png")
        print(name, "muzzles", [(m["name"], m["x"], m["y"], m["dir"]) for m in muz], "nozzles", [(n["x"], n["y"]) for n in nz])


main()
