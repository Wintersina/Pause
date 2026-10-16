#!/usr/bin/env python3
"""Defs for the three rebuilt elites: cellPixels 384, the 10-cell map, muzzles / nozzles measured on the built hover cell 3, cellWorldSize so the
DRAWN silhouette (flames included: ShipTierTest measures the flight cells' alpha) lands on a chosen world size, a preview per ship.
Muzzle anchors are shares of the hull's alpha bbox (read off the master), snapped onto solid art. Nozzles are the engine-bell mouths found by
build_strips.py (build_info.json), where the baked flame leaves.
  python3 points.py <preview dir> [--no-write]"""
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import build_strips as bs

ART = bs.ART
DEF = ART / "Resources/Elites/Defs"
S = bs.CELL
CELLS = {"flight": [3, 4, 5, 6], "parked": 0, "parkedIdle": 1, "liftoff": 2, "bankLeft": 7, "bankRight": 8, "tell": -1, "action": -1, "hit": -1, "damaged": 9}
# drawn long side in world units (flames included) -> cellWorldSize; engineColor follows the flame; muzzles as (name, share x, share y, dir)
CFG = {
    "cryo_siren":   dict(drawn=1.62, engine="#6FD8FF", muz=[("outlet", .5, .46, 270.0)]),
    "rimebreaker":  dict(drawn=1.72, engine="#6FD8FF", muz=[("prow", .5, .04, -1.0)]),
    "resin_warden": dict(drawn=1.85, engine="#FFB45A", muz=[("lpod", .06, .30, -1.0), ("rpod", .94, .30, -1.0)],
                         shot_core="#FFE9A8"),
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
    write = "--no-write" not in sys.argv
    info = json.load(open(HERE / "frames/build_info.json"))
    for name, cfg in bs.SHIPS.items():
        if name not in info:
            continue
        key, world = cfg["key"], cfg["world"]
        strip = Image.open(ART / "Resources/Elites" / world / f"{key}.png").convert("RGBA")
        cell0 = np.array(strip.crop((0, 0, S, S)))
        a = cell0[..., 3]
        ys, xs = np.nonzero(a > 128)
        x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
        w, h = x1 - x0, y1 - y0
        p = DEF / f"{key}.json"
        d = json.load(open(p))
        olds = list(d["muzzles"])
        muz = []
        for i, (nm, fx, fy, dr) in enumerate(CFG[name]["muz"]):
            x, y = solid_near(a, x0 + round(fx * w), y0 + round(fy * h), 24)
            muz.append(dict(name=nm, x=x, y=y, dir=dr))
        nz = [dict(name="lr"[i], x=int(round(m[0])), y=int(round(m[1])), dir=-1.0) for i, m in enumerate(info[name]["mouths"])]
        # drawn long side of the flight cells with the flames (alpha > 40), as ShipTierTest measures it
        best = 0
        for c in CELLS["flight"]:
            al = np.array(strip.crop((c * S, 0, (c + 1) * S, S)))[..., 3] > 40
            yy, xx = np.nonzero(al)
            best = max(best, xx.max() - xx.min() + 1, yy.max() - yy.min() + 1)
        d["cells"] = CELLS
        d["cellPixels"] = S
        d["cellWorldSize"] = round(CFG[name]["drawn"] * S / best, 3)
        d["muzzles"], d["nozzles"] = muz, nz
        d["engineColor"] = CFG[name]["engine"]
        d["exhaustScale"] = 0
        d["glowScale"] = 0.08
        if "shot_core" in CFG[name]:
            d["shotCore"] = CFG[name]["shot_core"]
        if write:
            open(p, "w").write(json.dumps(d, indent=2) + "\n")
        im = strip.crop((3 * S, 0, 4 * S, S))
        bg = Image.new("RGBA", (S, S), (24, 40, 70, 255)); bg.alpha_composite(im)
        dr = ImageDraw.Draw(bg)
        for m in muz: dr.ellipse((m["x"] - 8, m["y"] - 8, m["x"] + 8, m["y"] + 8), outline=(255, 255, 0, 255), width=2)
        for n in nz: dr.ellipse((n["x"] - 8, n["y"] - 8, n["x"] + 8, n["y"] + 8), outline=(0, 255, 255, 255), width=2)
        bg.save(out / f"points_{name}.png")
        print(name, "long side px", best, "cellWorldSize", d["cellWorldSize"], "muzzles", [(m["name"], m["x"], m["y"]) for m in muz], "nozzles", [(n["x"], n["y"]) for n in nz])


main()
