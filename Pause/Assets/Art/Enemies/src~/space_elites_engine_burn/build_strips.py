#!/usr/bin/env python3
"""Space elites, hard-surface redesign + engine-burn animation.

Masters (masters/v2/<file>.png, Codex, nose up, flat #00FF40 key background, empty engine bells) are keyed, scaled into a 384 px cell
and given a drawn engine burn cycle. 12 square cells of 384 px (the HD elite size):

  0 parked (engines off)   1 parked idle (pilot lights)   2 ignition (stub flames)
  3..10 burn cycle, 8 drawings, each engine on its own phase (the ship stays level: no bank cells, no swing)
  11 damaged (hull cracked, flames low and ragged)

  python3 build_strips.py [--out <dir>] [name ...]     default out: Resources/Elites/Space  (the live strips)
"""
import sys, json, random
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
RES = HERE.parents[2] / "Resources" / "Elites" / "Space"
CELL = 384
K = CELL // 192          # sizes below are written for a 192 cell and scaled by K
U = 4                    # one drawn "pixel" of flame = U cell px
BURN = 8
# per ship: master file, nozzle x as a share of the hull's width, bell width (192-cell px), flame colours (outer, mid, core), max hull w / h (384-cell px)
SHIPS = {
    "rift_lancer": dict(src="rift_lancer", nx=(.28, .5, .72), bell=17, w=300, h=270, flame=((70, 130, 255), (110, 225, 255), (255, 255, 255))),
    "eventide_bastion": dict(src="eventide_bastion", nx=(.2, .4, .6, .8), bell=22, w=350, h=270, flame=((150, 80, 255), (90, 225, 255), (255, 255, 255))),
    "orbit_reaver": dict(src="orbit_reaver", nx=(.30, .5, .70), dy=(-16, 0, -16), bell=25, w=330, h=270, flame=((255, 70, 215), (255, 160, 240), (255, 245, 255))),
    "singularity_hauler": dict(src="hauler_a", nx=(.25, .42, .58, .75), bell=26, w=340, h=270, flame=((255, 185, 40), (255, 235, 120), (255, 255, 235))),
}
# flame length per burn drawing (192-cell px), in the order the flight loop plays them (holds 6,3,2,3 ticks twice)
LENGTHS = (28, 34, 41, 35, 30, 38, 44, 36)
WIDTHS = (1.0, .92, .84, .92, 1.0, .9, .82, .9)
TOP = 10 * K // 2


def key_out(path):
    im = np.array(Image.open(path).convert("RGB")).astype(float)
    r, g, b = im[..., 0], im[..., 1], im[..., 2]
    gn = g - np.maximum(r, b)
    a = np.clip(1 - (gn - 35) / 80, 0, 1)
    gs = np.where(a < 1, np.minimum(g, np.maximum(r, b)), g)
    return Image.fromarray(np.dstack([r, gs, b, a * 255]).astype(np.uint8), "RGBA")


def hull(name):
    cfg = SHIPS[name]
    art = key_out(HERE / "masters" / "v2" / f"{cfg['src']}.png")
    art = art.crop(art.getchannel("A").point(lambda v: 255 if v > 128 else 0).getbbox())
    s = min(cfg["w"] / art.width, cfg["h"] / art.height)
    art = art.resize((max(1, round(art.width * s)), max(1, round(art.height * s))), Image.Resampling.LANCZOS)
    art.putalpha(art.getchannel("A").point(lambda v: 255 if v >= 128 else 0))   # crisp edge
    return art


def flame(canvas, x, y, length, width, cols, rnd, ragged=0.0):
    """One engine plume hanging from (x, y): stepped layers (outer, mid, core) on a canvas of U-px blocks."""
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


def burn_layer(nozzles, bell, length_for, cols, seed, ragged=0.0):
    rnd = random.Random(seed)
    canvas = Image.new("RGBA", (CELL // U, CELL // U))
    for i, (x, y) in enumerate(nozzles):
        L, k = length_for(i)
        flame(canvas, x, y, L * K, bell * K * k, cols, rnd, ragged)
    return canvas.resize((CELL, CELL), Image.Resampling.NEAREST)


def glow_dots(nozzles, bell, col, strength):
    im = Image.new("RGBA", (CELL, CELL))
    d = ImageDraw.Draw(im)
    for x, y in nozzles:
        r = max(3, round(bell * K * .2))
        d.rectangle((x - r, y - r - 8, x + r, y + r - 8), fill=col + (round(255 * strength),))
    return im


def cracks(im, bbox):
    d = ImageDraw.Draw(im)
    x0, y0, x1, y1 = bbox
    cx = (x0 + x1) // 2
    cy = y0 + (y1 - y0) // 3
    d.line([(cx - 4, cy - 28), (cx + 12, cy - 10), (cx, cy + 6), (cx + 18, cy + 26)], fill=(255, 79, 216, 235), width=4)
    d.line([(cx - 40, cy + 12), (cx - 24, cy + 24), (cx - 32, cy + 38)], fill=(110, 225, 255, 190), width=3)
    d.rectangle((cx + 16, cy + 24, cx + 20, cy + 28), fill=(255, 235, 250, 255))
    return im


def build(name, out_dir):
    cfg = SHIPS[name]
    art = hull(name)
    w, h = art.size
    ox, oy = (CELL - w) // 2, TOP
    hull_cell = Image.new("RGBA", (CELL, CELL))
    hull_cell.alpha_composite(art, (ox, oy))
    bottom = oy + h
    dys = cfg.get("dy", (0,) * len(cfg["nx"]))
    noz = [(ox + round(nx * w), bottom - 10 + dy) for nx, dy in zip(cfg["nx"], dys)]

    def compose(fx):
        c = Image.new("RGBA", (CELL, CELL))
        c.alpha_composite(fx)
        c.alpha_composite(hull_cell)
        return c
    cols, bell = cfg["flame"], cfg["bell"]
    cells = [hull_cell.copy(),                                                           # 0 parked, engines off
             compose(glow_dots(noz, bell, cols[1], .85)),                                # 1 pilot lights
             compose(burn_layer(noz, bell, lambda i: (12, .75), cols, 1))]               # 2 ignition
    for f in range(BURN):                                                                # 3..10 burn cycle
        def length_for(i, f=f):
            g = (f + 3 * i) % BURN
            return LENGTHS[g], WIDTHS[g]
        cells.append(compose(burn_layer(noz, bell, length_for, cols, 10 + f)))
    dmg = compose(burn_layer(noz, bell, lambda i: (24 + 5 * (i % 2), .8), cols, 99, ragged=.5))   # 11 damaged
    cracks(dmg, (ox, oy, ox + w, oy + h))
    cells.append(dmg)
    strip = Image.new("RGBA", (CELL * len(cells), CELL))
    for i, c in enumerate(cells):
        strip.alpha_composite(c, (i * CELL, 0))
    out_dir.mkdir(parents=True, exist_ok=True)
    strip.save(out_dir / f"space_elite_{name}.png")
    print(name, json.dumps(dict(hull_w=w, hull_h=h, ox=ox, oy=oy, nozzles=noz, bbox_cell3=cells[3].getbbox())))


if __name__ == "__main__":
    args = sys.argv[1:]
    out = RES
    if "--out" in args:
        i = args.index("--out"); out = Path(args[i + 1]); del args[i:i + 2]
    for n in (args or [n for n, c in SHIPS.items() if (HERE / "masters" / "v2" / f"{c['src']}.png").exists()]):
        build(n, out)
