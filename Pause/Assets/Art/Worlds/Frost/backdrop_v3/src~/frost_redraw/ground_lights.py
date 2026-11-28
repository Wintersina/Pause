#!/usr/bin/env python3
"""Frost's ground lights: find the lamps the mid tiles paint and write them into FrostGroundLights.cs (FrostGroundLightsTuning.Points).

    python3 ground_lights.py [--debug DIR]

Reads the 896 x 1792 masters (tiles_hd/v*/mid.png, the art the game draws halved) on the drawing's own 2 px cells:
  lamp   a cell of warm lit paint (hue 15-65, saturation > .3) at least LIFT brighter than the mean of its 7 x 7
         neighbourhood (a lamp dot, a lit window, a brazier, a lit crane cab -- not the ochre plant walls around it), the
         brightest of its 5 x 5 neighbours
  keep   the brightest first, none closer than SPACING cells to one kept already (the tile wraps both ways), at most CAP a tile
  kind   orange ones (hue < 30) burn as fires ('f'); the rest get the variant's mix (KINDS, seeded, so a re-run is stable):
         w window, b amber beacon, c cyan beacon, r rim blink, s strobe (FrostGroundLights.cs)
The points are master pixels from the top-left. Re-run after every mid redraw (the lamps move with the drawing); --debug
writes, per variant, the tile as drawn with every light ringed in its colour.
"""
import re
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage as nd

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
MASTERS = HERE / "tiles_hd"
CS = HERE.parents[5] / "Scripts/Worlds/Backdrop/FrostGroundLights.cs"
CELL = 2
LIFT = .03
SPACING = 14            # cells (~14 screen px on a phone: lamps never merge into a smear)
CAP = 24                # per tile (x the ~6 copies a phone sees: inside FrostGroundLightsTuning.Pool 192)
KINDS = {               # the mix per variant (the fires come from the drawing's own orange)
    1: {"w": .45, "b": .2, "c": .2, "s": .15},            # frozen ocean: camps on the floes, beacons on the masts
    2: {"r": .4, "w": .3, "c": .15, "b": .1, "s": .05},    # harbour: quay-edge rim lights, windows, crane beacons
    3: {"w": .5, "b": .25, "r": .1, "s": .15},              # tundra plant: windows, stack beacons
    4: {"c": .45, "w": .3, "s": .25},                       # glacier night: braziers (f) and cyan beacons
}
COLOR = {"w": (255, 184, 77), "f": (255, 148, 56), "b": (255, 184, 77), "c": (122, 237, 255), "r": (122, 237, 255), "s": (224, 242, 255)}


def hsv(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx, mn = rgb.max(-1), rgb.min(-1)
    d = mx - mn
    dd = np.where(d > 0, d, 1)
    h = np.where(mx == r, ((g - b) / dd) % 6, np.where(mx == g, (b - r) / dd + 2, (r - g) / dd + 4)) * 60
    return np.where(d > 0, h, 0), np.where(mx > 0, d / np.maximum(mx, 1e-6), 0), mx


def detect(v):
    a = np.asarray(Image.open(MASTERS / f"v{v}" / "mid.png").convert("RGB")).astype(np.float64) / 255
    H, W = a.shape[0] // CELL, a.shape[1] // CELL
    a = a.reshape(H, CELL, W, CELL, 3).mean((1, 3))
    h, s, val = hsv(a)
    score = val - nd.uniform_filter(val, 7, mode="wrap")
    warm = (h >= 15) & (h <= 65) & (s > .3) & (score > LIFT)
    peak = nd.maximum_filter(np.where(warm, score, -1), 5, mode="wrap")
    ys, xs = np.nonzero(warm & (score >= peak))
    order = np.argsort(-score[ys, xs], kind="stable")
    kept = []
    for i in order:
        y, x = int(ys[i]), int(xs[i])
        ok = True
        for (py, px, _) in kept:
            dy, dx = abs(py - y), abs(px - x)
            dy, dx = min(dy, H - dy), min(dx, W - dx)
            if dy * dy + dx * dx < SPACING * SPACING:
                ok = False
                break
        if ok:
            kept.append((y, x, float(h[y, x])))
            if len(kept) >= CAP:
                break
    rng = np.random.default_rng(1000 + v)
    mix = KINDS[v]
    names, p = list(mix), np.array(list(mix.values()))
    out = []
    for y, x, hue in sorted(kept):
        k = rng.choice(names, p=p / p.sum())
        if hue < 30:
            k = "f"
        out.append((x * CELL + CELL // 2, y * CELL + CELL // 2, str(k)))
    return out


def main():
    dbg = None
    if "--debug" in sys.argv:
        dbg = Path(sys.argv[sys.argv.index("--debug") + 1])
        dbg.mkdir(parents=True, exist_ok=True)
    rows = ['        "",']
    for v in (1, 2, 3, 4):
        pts = detect(v)
        kinds = {k: sum(1 for p in pts if p[2] == k) for k in "wfbcrs"}
        print(f"v{v}: {len(pts)} lights " + " ".join(f"{k} {n}" for k, n in kinds.items() if n))
        rows.append('        "' + ";".join(f"{x},{y},{k}" for x, y, k in pts) + '",')
        if dbg:
            im = Image.open(MASTERS / f"v{v}" / "mid.png").convert("RGB")
            im = Image.fromarray(np.minimum(255, np.asarray(im).astype(np.float64) * 1.9).astype(np.uint8))
            dr = ImageDraw.Draw(im)
            for x, y, k in pts:
                dr.ellipse((x - 9, y - 9, x + 9, y + 9), outline=COLOR[k], width=3)
            im.save(dbg / f"lights_v{v}.png")
    src = CS.read_text()
    block = "    public static readonly string[] Points =\n    {\n" + "\n".join(rows) + "\n    };\n"
    new, n = re.subn(r"(// <ground_lights>\n).*?(\s*// </ground_lights>)", lambda m: m.group(1) + block.rstrip("\n") + m.group(2), src, flags=re.S)
    assert n == 1, "markers not found in FrostGroundLights.cs"
    CS.write_text(new)
    print(f"-> {CS}")


if __name__ == "__main__":
    main()
