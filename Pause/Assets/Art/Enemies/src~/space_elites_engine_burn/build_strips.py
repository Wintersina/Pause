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
    "rift_lancer": dict(src="rift_lancer", nx=(.28, .5, .72), bell=17, mouths={0: (150, 262, 11, 8), 1: (193, 268, 16, 9), 2: (237, 262, 12, 8)}, w=300, h=270, flame=((70, 130, 255), (110, 225, 255), (255, 255, 255))),
    "eventide_bastion": dict(src="eventide_bastion", nx=(.2, .4, .6, .8), bell=22, w=350, h=270, flame=((150, 80, 255), (90, 225, 255), (255, 255, 255))),
    "orbit_reaver": dict(src="orbit_reaver", nx=(.30, .5, .70), dy=(-16, 0, -16), bell=25, w=330, h=270, flame=((255, 70, 215), (255, 160, 240), (255, 245, 255))),
    "singularity_hauler": dict(src="hauler_a", nx=(.25, .42, .58, .75), bell=26, mouths={1: (162, 266, 17, 8)}, w=340, h=270, flame=((255, 185, 40), (255, 235, 120), (255, 255, 235))),
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


def erode(m, r):
    o = m.copy()
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            o &= np.roll(np.roll(m, dy, 0), dx, 1)
    return o


def _component(mask, seed):
    from collections import deque
    H, W = mask.shape
    seen = np.zeros_like(mask)
    q = deque([seed]); seen[seed] = 1
    while q:
        y, x = q.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < H and 0 <= nx < W and mask[ny, nx] and not seen[ny, nx]:
                seen[ny, nx] = 1; q.append((ny, nx))
    return seen


def find_openings(hull_cell, seeds, manual):
    """The dark mouth of each engine bell on the bare hull cell: (cx, cy, rx, ry) in cell px.
    seeds = a point inside each bell; manual = {engine index: (cx, cy, rx, ry)} for bells the dark-pixel search cannot isolate."""
    arr = np.array(hull_cell).astype(int)
    lum = arr[..., 0] * .3 + arr[..., 1] * .59 + arr[..., 2] * .11
    e = erode((lum < 46) & (arr[..., 3] > 200), 2)
    ys, xs = np.nonzero(e)
    out = []
    for i, (sx, sy) in enumerate(seeds):
        if i in manual:
            out.append(manual[i]); continue
        j = ((ys - sy) ** 2 + (xs - sx) ** 2).argmin()
        yy, xx = np.nonzero(_component(e, (ys[j], xs[j])))
        x0, x1, y0, y1 = xx.min() - 2, xx.max() + 2, yy.min() - 2, yy.max() + 2
        out.append(((x0 + x1) / 2, (y0 + y1) / 2, (x1 - x0) / 2, (y1 - y0) / 2))
    return out


def flame(canvas, x, y, length, width, cols, rnd, ragged=0.0):
    """One engine plume leaving (x, y) downward: stepped layers (outer, mid, core) on a canvas of U-px blocks.
    It flares a little just past the root, then tapers to a tip."""
    d = ImageDraw.Draw(canvas)
    for layer, (col, lk, wk) in enumerate(zip(cols, (1.0, .72, .42), (1.0, .66, .34))):
        L = length * lk / U
        for row in range(int(L) + 1):
            t = row / max(1, L)
            hw = (width * wk / (2 * U)) * (1 + .18 * min(1.0, t / .25)) * (1 - t ** 1.7) ** .9
            if layer == 0 and row > L * .6 and ragged:
                hw *= 1 - ragged * rnd.random()
            hw = max(hw, .5 if row < L else 0)
            x0 = int(round(x / U - hw)); x1 = int(round(x / U + hw - .01))
            yy = int(y / U) + row
            d.line([(x0, yy), (max(x0, x1), yy)], fill=col + (255,))


def mouth_mask(op):
    """Where a flame may be drawn for a bell mouth: inside the mouth ellipse, or anywhere at/below its centre line."""
    cx, cy, rx, ry = op
    m = Image.new("L", (CELL, CELL), 0)
    ImageDraw.Draw(m).rectangle((0, int(cy), CELL, CELL), fill=255)
    ImageDraw.Draw(m).ellipse((cx - rx, cy - ry, cx + rx, cy + ry), fill=255)
    return m


def burn_layer(mouths, length_for, cols, seed, ragged=0.0):
    """Flames drawn OVER the hull: each leaves its bell's mouth (root just inside the opening, the white-hot core filling it)."""
    rnd = random.Random(seed)
    layer = Image.new("RGBA", (CELL, CELL))
    for i, op in enumerate(mouths):
        cx, cy, rx, ry = op
        L, k = length_for(i)
        c = Image.new("RGBA", (CELL // U, CELL // U))
        flame(c, cx, cy - ry * .5, L * K + ry * .5, 2 * rx * .88 * k, cols, rnd, ragged)
        c = c.resize((CELL, CELL), Image.Resampling.NEAREST)
        c.putalpha(Image.fromarray(np.minimum(np.array(c.getchannel("A")), np.array(mouth_mask(op))), "L"))
        layer.alpha_composite(c)
    return layer


def pilot_lights(mouths, col, strength):
    """Cell 1: each bell's mouth glows from inside (a small lit ellipse, hard steps), nothing leaves it yet."""
    im = Image.new("RGBA", (CELL, CELL))
    d = ImageDraw.Draw(im)
    for cx, cy, rx, ry in mouths:
        a = round(255 * strength)
        d.ellipse((cx - rx * .72, cy - ry * .6, cx + rx * .72, cy + ry * .6), fill=col + (a,))
        d.ellipse((cx - rx * .36, cy - ry * .3, cx + rx * .36, cy + ry * .3), fill=(255, 255, 255, a))
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
    seeds = [(ox + round(nx * w), bottom - 14 + dy) for nx, dy in zip(cfg["nx"], cfg.get("dy", (0,) * len(cfg["nx"])))]
    mouths = find_openings(hull_cell, seeds, cfg.get("mouths", {}))

    def compose(fx):                     # the flame is in FRONT of the hull: it comes out of the bell mouth towards the viewer
        c = hull_cell.copy()
        c.alpha_composite(fx)
        return c
    cols = cfg["flame"]
    cells = [hull_cell.copy(),                                                           # 0 parked, engines off
             compose(pilot_lights(mouths, cols[1], .85)),                                # 1 pilot lights
             compose(burn_layer(mouths, lambda i: (12, .75), cols, 1))]                  # 2 ignition
    for f in range(BURN):                                                                # 3..10 burn cycle
        def length_for(i, f=f):
            g = (f + 3 * i) % BURN
            return LENGTHS[g], WIDTHS[g]
        cells.append(compose(burn_layer(mouths, length_for, cols, 10 + f)))
    dmg = compose(burn_layer(mouths, lambda i: (24 + 5 * (i % 2), .8), cols, 99, ragged=.5))   # 11 damaged
    cracks(dmg, (ox, oy, ox + w, oy + h))
    cells.append(dmg)
    strip = Image.new("RGBA", (CELL * len(cells), CELL))
    for i, c in enumerate(cells):
        strip.alpha_composite(c, (i * CELL, 0))
    out_dir.mkdir(parents=True, exist_ok=True)
    strip.save(out_dir / f"space_elite_{name}.png")
    print(name, json.dumps(dict(hull_w=w, hull_h=h, ox=ox, oy=oy, mouths=[tuple(round(v, 1) for v in m) for m in mouths], bbox_cell3=cells[3].getbbox())))


if __name__ == "__main__":
    args = sys.argv[1:]
    out = RES
    if "--out" in args:
        i = args.index("--out"); out = Path(args[i + 1]); del args[i:i + 2]
    for n in (args or [n for n, c in SHIPS.items() if (HERE / "masters" / "v2" / f"{c['src']}.png").exists()]):
        build(n, out)
