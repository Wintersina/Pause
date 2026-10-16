#!/usr/bin/env python3
"""Tide elites, ground-up redesign (Codex masters in masters/<name>.png, nose up, flat #00FF40 key, empty engine bells).

Each master is keyed, fitted into a 384 px cell and turned into the elite 7-cell flight strip (2688 x 384), the layout the defs already use:
  0 landed (engines off)   1 grounded idle (pilot lights)   2 lift-off ignition (stub flames)   3 hover (the flight frame)
  4 bank left   5 bank right   6 damaged (hull dimmed and cracked, flames low and ragged)
The hull sits at the same place in every cell (banks shear it a little); flames are drawn at each engine bell in hard steps.

  python3 build_strips.py [--out <dir>] [name ...]     default out: Resources/Elites/Tide
"""
import sys, json, random
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
RES = HERE.parents[2] / "Resources" / "Elites" / "Tide"
CELL = 384
K = 2
U = 4                      # one drawn "pixel" of flame = U cell px
# per ship: master file, hull fit box (w, h, 384-cell px), nozzles as (share of hull width, share of hull height), bell width (192-cell px),
# flame colours (outer, mid, core), glow colour of the damaged cracks, hover flame length
SHIPS = {
    "riptide": dict(w=240, h=290, nz=((.427, .95), (.571, .95)), bell=11, flame=((60, 130, 255), (130, 225, 255), (255, 255, 255)), len=20, crack=(255, 176, 32)),
    "bathyscaphe": dict(src="bathyscaphe_orig_repaint", w=262, h=238, nz=((.332, .95), (.672, .95)), bell=13, flame=((60, 170, 255), (150, 235, 255), (255, 255, 255)), len=16, crack=(255, 255, 255)),
    "trawler": dict(w=296, h=270, nz=((.386, .955), (.616, .955)), bell=15, flame=((70, 150, 255), (150, 215, 255), (255, 255, 255)), len=16, crack=(255, 210, 31)),
    "thunderfin": dict(w=276, h=260, nz=((.424, .955), (.574, .955)), bell=13, flame=((120, 80, 255), (150, 230, 255), (255, 255, 255)), len=18, crack=(255, 240, 138)),
    "pearl_warden": dict(w=270, h=262, nz=((.219, .945), (.784, .945)), bell=21, flame=((40, 200, 180), (160, 255, 232), (255, 255, 255)), len=16, crack=(191, 255, 240)),
}
FLAME_ROOM = 40 * K       # vertical room kept under the hull for the flames


def key_out(path):
    im = np.array(Image.open(path).convert("RGB")).astype(float)
    r, g, b = im[..., 0], im[..., 1], im[..., 2]
    gn = g - np.maximum(r, b)
    a = np.clip(1 - (gn - 35) / 80, 0, 1)
    gs = np.where(a < 1, np.minimum(g, np.maximum(r, b)), g)
    return Image.fromarray(np.dstack([r, gs, b, a * 255]).astype(np.uint8), "RGBA")


def hull(name):
    cfg = SHIPS[name]
    art = key_out(HERE / "masters" / f"{cfg.get('src', name)}.png")
    art = art.crop(art.getchannel("A").point(lambda v: 255 if v > 128 else 0).getbbox())
    s = min(cfg["w"] / art.width, cfg["h"] / art.height)
    art = art.resize((max(1, round(art.width * s)), max(1, round(art.height * s))), Image.Resampling.LANCZOS)
    art.putalpha(art.getchannel("A").point(lambda v: 255 if v >= 128 else 0))   # crisp edge
    lift = cfg.get("lift", 1.0)                      # readability with no outline: raise the value of a hull that sits too close to the sea
    if lift != 1.0:
        a = np.asarray(art).astype(np.float32).copy()
        rgb = a[..., :3] / 255.0
        a[..., :3] = np.clip((rgb ** (1 / lift)) * 255.0, 0, 255)
        art = Image.fromarray(a.astype(np.uint8), "RGBA")
    return art


def flame(canvas, x, y, length, width, cols, rnd, ragged=0.0):
    d = ImageDraw.Draw(canvas)
    for layer, (col, lk, wk) in enumerate(zip(cols, (1.0, .72, .42), (1.0, .66, .34))):
        L = length * lk / U
        for row in range(int(L) + 1):
            t = row / max(1, L)
            hw = (width * wk / (2 * U)) * (1 - t ** 1.7) ** .9
            if layer == 0 and row > L * .6 and ragged:
                hw *= 1 - ragged * rnd.random()
            hw = max(hw, .5 if row < L else 0)
            x0 = int(round(x / U - hw)); x1 = int(round(x / U + hw - .01))
            yy = int(y / U) + row
            d.line([(x0, yy), (max(x0, x1), yy)], fill=col + (255,))


def burn_layer(nozzles, bell, lengths, cols, seed, ragged=0.0):
    rnd = random.Random(seed)
    canvas = Image.new("RGBA", (CELL // U, CELL // U))
    for (x, y), L in zip(nozzles, lengths):
        flame(canvas, x, y, L * K, bell * K, cols, rnd, ragged)
    return canvas.resize((CELL, CELL), Image.Resampling.NEAREST)


def glow_dots(nozzles, bell, col, strength):
    im = Image.new("RGBA", (CELL, CELL))
    d = ImageDraw.Draw(im)
    for x, y in nozzles:
        r = max(3, round(bell * K * .2))
        d.rectangle((x - r, y - r - 6, x + r, y + r - 6), fill=col + (round(255 * strength),))
    return im


def bank(cell, sign, top, h):
    """Bank by a gentle shear (the nose leans into the turn) and a 3% narrowing; returns (cell, dx at a given y)."""
    a = np.asarray(cell)
    yy, xx = np.indices((CELL, CELL))
    mid = top + h * .55
    shift = np.rint(sign * 4 * (mid - yy) / (h * .5)).astype(int)
    src = np.clip(np.rint(CELL / 2 + (xx - shift - CELL / 2) / .97).astype(int), 0, CELL - 1)
    return Image.fromarray(a[yy, src], "RGBA"), (lambda y: int(round(sign * 4 * (mid - y) / (h * .5))))


def dim(im, f):
    a = np.asarray(im).astype(np.float32).copy()
    s = a[..., 3] > 0
    a[s, :3] *= f
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def cracks(im, bbox, col):
    d = ImageDraw.Draw(im)
    x0, y0, x1, y1 = bbox
    cx = (x0 + x1) // 2
    cy = y0 + (y1 - y0) // 3
    d.line([(cx - 4, cy - 28), (cx + 12, cy - 10), (cx, cy + 6), (cx + 18, cy + 26)], fill=(10, 14, 24, 255), width=7)
    d.line([(cx - 4, cy - 28), (cx + 12, cy - 10), (cx, cy + 6), (cx + 18, cy + 26)], fill=col + (255,), width=3)
    d.line([(cx - 40, cy + 12), (cx - 24, cy + 24), (cx - 32, cy + 38)], fill=(10, 14, 24, 255), width=5)
    d.line([(cx - 40, cy + 12), (cx - 24, cy + 24), (cx - 32, cy + 38)], fill=col + (200,), width=2)
    d.rectangle((cx + 16, cy + 24, cx + 20, cy + 28), fill=(255, 250, 240, 255))
    return im


def build(name, out_dir):
    cfg = SHIPS[name]
    art = hull(name)
    w, h = art.size
    ox = (CELL - w) // 2
    oy = (CELL - h - FLAME_ROOM + 20) // 2
    hull_cell = Image.new("RGBA", (CELL, CELL))
    hull_cell.alpha_composite(art, (ox, oy))
    noz = [(ox + round(fx * w), oy + round(fy * h)) for fx, fy in cfg["nz"]]
    cols, bell, ln = cfg["flame"], cfg["bell"], cfg["len"] * 1.4

    def compose(base, flames):
        c = Image.new("RGBA", (CELL, CELL))
        c.alpha_composite(flames)
        c.alpha_composite(base)
        return c
    n = len(noz)
    cells = [dim(hull_cell, .82),
             compose(hull_cell, glow_dots(noz, bell, cols[1], .85)),
             compose(hull_cell, burn_layer(noz, bell, [ln * .45] * n, cols, 1)),
             compose(hull_cell, burn_layer(noz, bell, [ln * (1 + .08 * (i % 2)) for i in range(n)], cols, 2))]
    for sign in (-1, 1):
        b, dx = bank(hull_cell, sign, oy, h)
        nz2 = [(x + dx(y), y) for x, y in noz]
        cells.append(compose(b, burn_layer(nz2, bell, [ln * 1.1] * n, cols, 3 + sign)))
    dmg = compose(dim(hull_cell, .88), burn_layer(noz, bell, [ln * (.55 + .25 * (i % 2)) for i in range(n)], cols, 99, ragged=.5))
    cracks(dmg, (ox, oy, ox + w, oy + h), cfg["crack"])
    cells.append(dmg)
    strip = Image.new("RGBA", (CELL * len(cells), CELL))
    for i, c in enumerate(cells):
        strip.alpha_composite(c, (i * CELL, 0))
    out_dir.mkdir(parents=True, exist_ok=True)
    strip.save(out_dir / f"tide_elite_{name}.png")
    print(name, json.dumps(dict(hull_w=w, hull_h=h, ox=ox, oy=oy, nozzles=noz, bbox_cell3=cells[3].getbbox())))


if __name__ == "__main__":
    args = sys.argv[1:]
    out = RES
    if "--out" in args:
        i = args.index("--out"); out = Path(args[i + 1]); del args[i:i + 2]
    for nme in (args or [n for n in SHIPS if (HERE / "masters" / f"{SHIPS[n].get('src', n)}.png").exists()]):
        build(nme, out)
