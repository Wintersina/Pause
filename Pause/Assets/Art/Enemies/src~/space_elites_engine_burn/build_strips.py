#!/usr/bin/env python3
"""Space elites, hard-surface redesign + engine-burn animation.

Masters (masters/v2/<file>.png, Codex, nose up, flat #00FF40 key background, empty engine bells) are keyed, scaled into a 384 px cell
and given a drawn engine burn cycle. 12 square cells of 384 px (the HD elite size):

  0 parked (engines off)   1 parked idle (pilot lights)   2 ignition (stub flames)
  3..10 burn cycle, 8 drawings, each engine on its own phase (the ship stays level: no bank cells, no swing)
  11 damaged (hull cracked, flames low and ragged)

  python3 build_strips.py [--out <dir>] [name ...]     default out: Resources/Elites/Space  (the live strips)
"""
import sys, json, random, math
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


BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.0 + 1 / 32.0   # 4x4 ordered-dither thresholds


def bayer(x, y):
    return BAYER[y % 4, x % 4]


def lerp_col(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def neck(t):
    """Plume width vs the bore, by distance from the exit: a slight narrowing (vena contracta), then the flare."""
    if t < .08:
        return 1 - .18 * (t / .08)
    if t < .30:
        return .82 + .26 * ((t - .08) / .22)
    return 1.08


def plume_layer(cx, root_y, length, width, col, alpha, len_k, wid_k, unit, jitter, rnd, ragged=0.0, dissolve=True):
    """One layer of a plume on a canvas of `unit`-px blocks, root at (cx, root_y), going down. Returns an RGBA cell-size image.
    The edge flickers (`jitter` blocks), the far end dissolves in an ordered dither, and the root narrows then flares."""
    n = CELL // unit
    canvas = Image.new("RGBA", (n, n))
    d = ImageDraw.Draw(canvas)
    L = length * len_k / unit
    fill = col + (255,)
    for row in range(int(L) + 1):
        t = row / max(1, L)
        hw = (width * wid_k / (2 * unit)) * neck(t) * (1 - t ** 1.7) ** .9
        hw += jitter * (rnd.random() - .5) * 1.6
        if ragged and row > L * .55:
            hw *= 1 - ragged * rnd.random()
        hw = max(hw, .5 if row < L else 0)
        x0 = int(round(cx / unit - hw)); x1 = max(x0, int(round(cx / unit + hw - .01)))
        yy = int(root_y / unit) + row
        if dissolve and t > .78:
            keep = (1 - t) / .22
            for bx in range(x0, x1 + 1):
                if keep > bayer(bx, yy):
                    d.point((bx, yy), fill=fill)
        else:
            d.line([(x0, yy), (x1, yy)], fill=fill)
    out = canvas.resize((CELL, CELL), Image.Resampling.NEAREST)
    if alpha < 255:
        out.putalpha(out.getchannel("A").point(lambda v: v * alpha // 255))
    return out


def mouth_grid(op):
    cx, cy, rx, ry = op
    yy, xx = np.mgrid[0:CELL, 0:CELL]
    e = ((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2
    return xx, yy, e


def keep_mask(op):
    """Soft root: below the mouth's centre line anything goes; above it the plume only lives inside the mouth and fades out
    through an ordered-dither band at the ellipse edge (no hard clip line)."""
    cx, cy, rx, ry = op
    xx, yy, e = mouth_grid(op)
    level = np.clip((1 - e) / .38, 0, 1)
    inside = level > BAYER[yy % 4, xx % 4]
    return Image.fromarray(np.where(yy >= int(cy), 255, np.where(inside, 255, 0)).astype(np.uint8), "L")


def inner_glow(op, cols, strength):
    """The bell's inside lit: hot white-yellow core -> flame colours -> engine colour at the wall, 4 dithered ramp steps."""
    cx, cy, rx, ry = op
    xx, yy, e = mouth_grid(op)
    r = np.sqrt(e)
    ramp = [(255, 250, 205), cols[2], cols[1], cols[0]]
    t = np.clip(r * 1.05 + (BAYER[yy % 4, xx % 4] - .5) * .30, 0, .999)
    idx = (t * 4).astype(int)
    img = np.zeros((CELL, CELL, 4), np.uint8)
    for k, c in enumerate(ramp):
        m = (idx == k) & (r <= 1)
        a = 255 if k < 3 else 150                       # the outermost step is translucent: the dark wall still shows through
        img[m] = (*c, round(a * strength))
    return Image.fromarray(img, "RGBA")


def spill(hull_cell, mouths, col, strength):
    """Emissive spill: hull pixels just round each mouth take the engine colour (dithered steps, strongest at the lip)."""
    arr = np.array(hull_cell).astype(float)
    solid = arr[..., 3] > 0
    for i, op in enumerate(mouths):
        cx, cy, rx, ry = op
        xx, yy, e = mouth_grid(op)
        dist = (np.sqrt(e) - 1) * min(rx, ry * 1.6)
        ring = solid & (dist >= 0) & (dist <= 7)
        amt = strength[i] * .5 * (1 - np.clip(dist, 0, 7) / 7)
        step = np.floor(amt / .12 + BAYER[yy % 4, xx % 4]) * .12
        amt = np.where(ring, step, 0)[..., None]
        arr[..., :3] = arr[..., :3] * (1 - amt) + np.array(col, float) * amt
    return Image.fromarray(arr.clip(0, 255).astype(np.uint8), "RGBA")


def halo(mouths, col, strength, lens):
    """Very soft low-alpha halo round the mouth and the first stretch of the plume (quantised, dithered)."""
    out = Image.new("RGBA", (CELL, CELL))
    yy, xx = np.mgrid[0:CELL, 0:CELL]
    for i, (cx, cy, rx, ry) in enumerate(mouths):
        if strength[i] <= 0:
            continue
        lpx = lens[i] * K
        hy = cy + ry * .6 + .12 * lpx
        r = np.sqrt(((xx - cx) / (rx * 1.9)) ** 2 + ((yy - hy) / (rx * 1.35 + .16 * lpx)) ** 2)
        a = 78 * strength[i] * np.clip(1 - r, 0, 1) ** 1.6
        q = np.floor(a / 13 + BAYER[yy % 4, xx % 4]) * 13
        q = np.where(yy >= cy - ry * .5, q, 0)
        img = np.zeros((CELL, CELL, 4), np.uint8)
        img[..., :3] = col
        img[..., 3] = np.clip(q, 0, 255)
        out.alpha_composite(Image.fromarray(img, "RGBA"))
    return out


def lip_mask(hull_cell, op):
    """The bell's lower lip: the hull rim just under the mouth. Re-composited OVER the flame so the plume comes out from behind it."""
    cx, cy, rx, ry = op
    xx, yy, e = mouth_grid(op)
    dist_v = (np.sqrt(e) - 1) * ry
    solid = np.array(hull_cell.getchannel("A")) > 128
    m = solid & (e > 1) & (dist_v <= 6.5) & (yy > cy + ry * .25) & (np.abs(xx - cx) <= rx + 3)
    return Image.fromarray(np.where(m, 255, 0).astype(np.uint8), "L")


def engine_cell(hull_cell, mouths, cols, lens, wks, pulses, rnd, ragged=0.0, strong=None, glow=1.0, lit_spill=1.0):
    """One drawing: hull, spill, inner glow, halo, flames (soft root, core + sheath, tip dissolve, sparks, shock diamonds), lip over it all."""
    n = len(mouths)
    strong = strong or [False] * n
    hull_t = spill(hull_cell, mouths, cols[1], [lit_spill * (.55 + .45 * p) * (1 if lens[i] > 0 else .6) for i, p in enumerate(pulses)])
    c = hull_t.copy()
    for i, op in enumerate(mouths):
        c.alpha_composite(inner_glow(op, cols, glow * (.8 + .2 * pulses[i])))
    c.alpha_composite(halo(mouths, cols[1], [p * (1 if lens[i] > 0 else .45) for i, p in enumerate(pulses)], lens))
    dark = lerp_col(cols[0], (20, 10, 50), .45)
    for i, op in enumerate(mouths):
        L = lens[i]
        if L <= 0:
            continue
        cx, cy, rx, ry = op
        root = cy - ry * .5
        length = L * K + ry * .5
        width = 2 * rx * .88 * wks[i]
        p = pulses[i]
        mid = lerp_col(cols[1], (255, 255, 255), .16 * p)
        fl = Image.new("RGBA", (CELL, CELL))
        for col, alpha, lk, wk, unit, jit, dis in ((dark, 130, 1.12, 1.3, 2, 1.0, True), (cols[0], 255, 1.0, 1.0, 4, 0, True),
                                                    (mid, 255, .72, .66, 4, 0, True), (cols[2], 255, .42, .34, 4, 0, False)):
            fl.alpha_composite(plume_layer(cx, root, length, width, col, alpha, lk, wk, unit, jit, rnd, ragged if lk == 1.0 else 0, dis))
        d = ImageDraw.Draw(fl)
        if strong[i]:                                    # shock diamonds along the axis on the strongest drawings
            for t in (.40, .64):
                y0 = int(root + length * .72 * t * 1.1)
                for dy in range(-4, 5):
                    hw = 4 - abs(dy)
                    d.line([(cx - hw, y0 + dy), (cx + hw, y0 + dy)], fill=(255, 255, 255, 255))
        tip_y = int(root + length)
        for _ in range(1 + (1 if strong[i] else 0) + rnd.randint(0, 1)):    # 1-3 detached embers past the tip
            sx = int(cx + rnd.randint(-5, 5)); sy = tip_y + rnd.randint(3, 12)
            if sy + 2 <= CELL - 8:
                d.rectangle((sx, sy, sx + 1, sy + 1), fill=(cols[1] if rnd.random() < .6 else cols[2]) + (255,))
        fl.putalpha(Image.fromarray(np.minimum(np.array(fl.getchannel("A")), np.array(keep_mask(op))), "L"))
        c.alpha_composite(fl)
    for op in mouths:                                    # the bell's lower lip stays in front of the flame root
        lip = hull_t.copy()
        lip.putalpha(lip_mask(hull_cell, op))
        c.alpha_composite(lip)
    return c


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

    cols = cfg["flame"]
    n = len(mouths)
    rnd = random.Random(7)
    cells = [hull_cell.copy(),                                                                       # 0 parked, engines off
             engine_cell(hull_cell, mouths, cols, [0] * n, [1] * n, [.8] * n, rnd, glow=.7, lit_spill=.6)]   # 1 pilot lights
    cells.append(engine_cell(hull_cell, mouths, cols, [12] * n, [.75] * n, [.9] * n, rnd))           # 2 ignition
    for f in range(BURN):                                                                            # 3..10 burn cycle
        gs = [(f + 3 * i) % BURN for i in range(n)]
        pulses = [.5 + .5 * math.cos(2 * math.pi * g / BURN) for g in gs]
        cells.append(engine_cell(hull_cell, mouths, cols, [LENGTHS[g] for g in gs], [WIDTHS[g] for g in gs], pulses,
                                 random.Random(100 + f), strong=[LENGTHS[g] >= 41 for g in gs]))
    dmg = engine_cell(hull_cell, mouths, cols, [24 + 5 * (i % 2) for i in range(n)], [.8] * n, [.55 + .3 * (i % 2) for i in range(n)],
                      random.Random(99), ragged=.5, lit_spill=.7)                                     # 11 damaged
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
