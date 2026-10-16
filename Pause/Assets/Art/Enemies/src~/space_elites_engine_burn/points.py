#!/usr/bin/env python3
"""Measure each Space elite's muzzles + nozzles on the built 384 px strip and write them into the def JSON (cellPixels 384).
Muzzles: lancer = nose tip; bastion = the twin-barrel turrets on the shoulders; reaver = the two magenta claw tips; hauler = the pincer tips.
Nozzles = the engine-mouth centres the burn was drawn at (build_strips.py), nudged onto solid art. Writes a preview per ship.
  python3 points.py <preview dir>"""
import json, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, str(Path(__file__).parent))
import build_strips as bs

R = Path(__file__).resolve().parents[3]
DEF = R / "Resources/Elites/Defs"
STRIP = R / "Resources/Elites/Space"
S = bs.CELL


def solid_near(a, x, y, r):
    best = None
    for yy in range(max(0, y - r), min(S, y + r + 1)):
        for xx in range(max(0, x - r), min(S, x + r + 1)):
            if a[yy, xx] >= 200:
                dd = (xx - x) ** 2 + (yy - y) ** 2
                if best is None or dd < best[0]:
                    best = (dd, xx, yy)
    return best[1], best[2]


def magenta_tip(arr, left):
    rgb = arr[..., :3].astype(int)
    m = (rgb[..., 0] > 190) & (rgb[..., 2] > 130) & (rgb[..., 1] < 120) & (arr[..., 3] > 200)
    if left:
        m[:, S // 2:] = False
    else:
        m[:, :S // 2] = False
    ys, xs = np.nonzero(m)
    top = ys <= ys.min() + 10
    return int(xs[top].mean()), int(ys[top].mean())


def top_of(a, left, cx, w):
    m = a > 200
    xs_ok = np.zeros_like(m)
    if left:
        xs_ok[:, :cx - int(.2 * w)] = True
    else:
        xs_ok[:, cx + int(.2 * w):] = True
    ys, xs = np.nonzero(m & xs_ok)
    i = ys.argmin()
    return int(xs[ys <= ys.min() + 8].mean()), int(ys.min())


def main():
    out = Path(sys.argv[1])
    for name, cfg in bs.SHIPS.items():
        arr = np.array(Image.open(STRIP / f"space_elite_{name}.png").convert("RGBA").crop((0, 0, S, S)))   # cell 0: the bare hull
        a = arr[..., 3]
        ys, xs = np.nonzero(a > 128)
        x0, x1, y0, y1 = int(xs.min()), int(xs.max()), int(ys.min()), int(ys.max())
        cx, w, h = (x0 + x1) // 2, x1 - x0 + 1, y1 - y0 + 1
        if name == "rift_lancer":
            muz = [dict(name="prong", x=cx, y=y0 + 16, dir=-1.0)]
        elif name == "eventide_bastion":
            muz = [dict(name="lpod", x=x0 + round(.207 * w), y=y0 + round(.22 * h), dir=270.0),
                   dict(name="rpod", x=x1 - round(.207 * w), y=y0 + round(.22 * h), dir=270.0)]
        elif name == "orbit_reaver":
            lx, ly = magenta_tip(arr, True); rx, ry = magenta_tip(arr, False)
            muz = [dict(name="lclaw", x=lx, y=ly + 8, dir=90.0), dict(name="rclaw", x=rx, y=ry + 8, dir=90.0)]
        else:
            lx, ly = top_of(a, True, cx, w); rx, ry = top_of(a, False, cx, w)
            muz = [dict(name="lclaw", x=lx, y=ly + 8, dir=90.0), dict(name="rclaw", x=rx, y=ry + 8, dir=90.0)]
        for m in muz:
            m["x"], m["y"] = solid_near(a, m["x"], m["y"], 24)
        dys = cfg.get("dy", (0,) * len(cfg["nx"]))
        nz = []
        for i, (nx, dy) in enumerate(zip(cfg["nx"], dys)):
            x, y = solid_near(a, x0 + round(nx * w), y1 - 36 + dy, 28)
            nz.append(dict(name="abcd"[i], x=x, y=y, dir=-1.0))
        p = DEF / f"space_elite_{name}.json"
        d = json.load(open(p))
        d["muzzles"], d["nozzles"], d["cellPixels"] = muz, nz, S
        open(p, "w").write(json.dumps(d, indent=2) + "\n")
        im = Image.open(STRIP / f"space_elite_{name}.png").convert("RGBA").crop((3 * S, 0, 4 * S, S))
        bg = Image.new("RGBA", (S, S), (30, 24, 60, 255)); bg.alpha_composite(im)
        dr = ImageDraw.Draw(bg)
        for m in muz: dr.ellipse((m["x"] - 8, m["y"] - 8, m["x"] + 8, m["y"] + 8), outline=(255, 255, 0, 255), width=2)
        for n in nz: dr.ellipse((n["x"] - 8, n["y"] - 8, n["x"] + 8, n["y"] + 8), outline=(0, 255, 255, 255), width=2)
        bg.save(out / f"points_{name}.png")
        print(name, "bbox", (x0, y0, x1, y1), "muzzles", [(m["name"], m["x"], m["y"]) for m in muz], "nozzles", [(n["x"], n["y"]) for n in nz])


main()
