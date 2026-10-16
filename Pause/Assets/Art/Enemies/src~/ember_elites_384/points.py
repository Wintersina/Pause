#!/usr/bin/env python3
"""Measure each Ember elite's muzzles + nozzles on the built 384 px strip, write the 384 px cell map + points + Ember cue colours into the def
JSONs, and draw a preview per ship (muzzles yellow, nozzles cyan, on flight cell 3).
Muzzles keep their 192 px positions mapped through the registration (hull top / centre, x2) and snapped onto solid art; MUZZLE_FIX overrides a
mapped point after a look at the preview. Nozzles = the centre of each bell's mouth (build_strips.find_openings): where the baked flame leaves.
  python3 points.py <preview dir> [--no-write]"""
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, str(Path(__file__).parent))
import build_strips as bs

R = Path(__file__).resolve().parents[3]
DEF = R / "Resources/Elites/Defs"
STRIP = R / "Resources/Elites/Ember"
S = bs.CELL
# the legacy 192 px muzzles (name, x, y, dir) read off the old defs
OLD = {
    "sunstoke": [("lance", 96, 19, -1.0)],
    "coalrunner": [("left", 40, 91, 180.0), ("right", 153, 91, 0.0)],
    "brass_vulture": [("claw", 94, 25, -1.0)],
    "kilnback": [("l", 67, 107, 270.0), ("r", 125, 107, 270.0)],
    "ash_wraith": [("beak", 96, 22, -1.0)],
    "cauterizer": [("cannon", 96, 23, -1.0)],
}
MUZZLE_FIX = {}        # {ship: {muzzle name: (x, y)}} 384 px overrides after looking at the previews
ENGINE = {"sunstoke": "#FF9A3C", "coalrunner": "#FF9A3C", "brass_vulture": "#FF9A3C", "kilnback": "#FF9A3C", "ash_wraith": "#7FD8FF", "cauterizer": "#FF9A3C"}
SHOT = "#FF8A2A"; CORE = "#FFE9B8"; HEART = "#FFC24A"      # Ember cue: orange / amber, no pink outside Space
CELLS = {"flight": [3, 4], "parked": 0, "parkedIdle": 1, "liftoff": 2, "bankLeft": 5, "bankRight": 6,
         "tell": -1, "action": -1, "hit": -1, "damaged": 7}


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
        if not (STRIP / f"ember_elite_{name}.png").exists():
            continue
        arr = np.array(Image.open(STRIP / f"ember_elite_{name}.png").convert("RGBA").crop((0, 0, S, S)))   # cell 0: the bare hull
        a = arr[..., 3]
        ys, xs = np.nonzero(a > 128)
        x0, x1, y0 = int(xs.min()), int(xs.max()), int(ys.min())
        ox0, oy0, ox1, _ = bs.LEGACY[name]
        ocx, cx = (ox0 + ox1) / 2, (x0 + x1) / 2
        sc = (x1 - x0) / (ox1 - ox0)
        muz = []
        for nm, mx, my, dr in OLD[name]:
            tx, ty = round(cx + (mx - ocx) * sc), round(y0 + (my - oy0) * sc)
            if nm in MUZZLE_FIX.get(name, {}):
                tx, ty = MUZZLE_FIX[name][nm]
            else:
                tx, ty = solid_near(a, tx, ty, 24)
            muz.append(dict(name=nm, x=tx, y=ty, dir=dr))
        mouths = bs.find_openings(Image.fromarray(arr), cfg["seeds"], cfg.get("mouths", {}))
        nz = [dict(name="abc"[i], x=int(round(mx)), y=int(round(my)), dir=-1.0) for i, (mx, my, _, _) in enumerate(mouths)]
        p = DEF / f"ember_elite_{name}.json"
        d = json.load(open(p))
        d["muzzles"], d["nozzles"], d["cellPixels"] = muz, nz, S
        d["cells"] = CELLS
        d["exhaustScale"], d["glowScale"] = 0, 0.09
        d.setdefault("maxBank", 3)
        d["engineColor"], d["shotColor"], d["shotCore"], d["heartColor"] = ENGINE[name], SHOT, CORE, HEART
        if write:
            open(p, "w").write(json.dumps(d, indent=2) + "\n")
        im = Image.open(STRIP / f"ember_elite_{name}.png").convert("RGBA").crop((3 * S, 0, 4 * S, S))
        bg = Image.new("RGBA", (S, S), (30, 24, 60, 255)); bg.alpha_composite(im)
        dr = ImageDraw.Draw(bg)
        for m in muz: dr.ellipse((m["x"] - 8, m["y"] - 8, m["x"] + 8, m["y"] + 8), outline=(255, 255, 0, 255), width=2)
        for n in nz: dr.ellipse((n["x"] - 8, n["y"] - 8, n["x"] + 8, n["y"] + 8), outline=(0, 255, 255, 255), width=2)
        bg.save(out / f"points_{name}.png")
        print(name, "muzzles", [(m["name"], m["x"], m["y"], m["dir"]) for m in muz], "nozzles", [(n["x"], n["y"]) for n in nz])


main()
