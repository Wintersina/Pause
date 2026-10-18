#!/usr/bin/env python3
"""Space elites, hard-surface redesign + engine-burn animation.

Masters (masters/v2/<file>.png, Codex, nose up, flat #00FF40 key background, empty engine bells) are keyed, scaled into a 384 px cell
and given a drawn engine burn cycle. 12 square cells of 384 px (the HD elite size):

  0 parked (engines off)   1 parked idle (pilot lights)   2 ignition (stub flames)
  3..10 burn cycle, 8 drawings, each engine on its own phase (the ship stays level: no bank cells, no swing)
  11..16 damaged burn loop (11 in the main strip, 12..16 in the companion strip <name>_damaged.png): the painted damaged hull (masters/damaged/<name>.png, Codex) with its painted flames ERASED (bells restored
         from cell 0) and a weaker, ragged, flickering burn drawn on every engine, six drawings, each engine on its own phase;
         one engine sputters out for a beat now and then. Cell 11 is also the drawing the debris is cut from.

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
# the damaged burn loop: flame length per drawing (192-cell px; 0 = the engine sputters out), width factor, drawings in the loop
DMG = 6
DLENGTHS = (17, 23, 13, 20, 10, 18)
DWIDTHS = (.8, .72, .64, .76, .6, .72)


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
    """One engine plume leaving (x, y) downward, on a canvas of U-px blocks (the same recipe as the Tide elites):
    a white core a few blocks wide, a mid-tone body and a darker outer layer, all hard-stepped (no dither). It widens slightly just below
    the exit, then tapers raggedly (the edges lose blocks, more towards the tip, different every drawing) and ends in a few
    detached single blocks."""
    d = ImageDraw.Draw(canvas)
    tip_row = 0
    for layer, (col, lk, wk) in enumerate(zip(cols, (1.0, .74, .36), (1.0, .66, .34))):
        L = length * lk / U
        for row in range(int(L) + 1):
            t = row / max(1, L)
            ramp = min(1.0, t / .25)
            hw = (width * wk / (2 * U)) * (.9 + .2 * ramp * (2 - ramp)) * (1 - t ** 1.8) ** .85
            if ragged and layer == 0 and row > L * .5:
                hw *= 1 - ragged * rnd.random()
            hw = max(hw, .5 if row < L else 0)
            x0 = int(round(x / U - hw)); x1 = max(x0, int(round(x / U + hw - .01)))
            yy = int(y / U) + row
            if layer < 2 and row > 1 and x1 - x0 >= 2:                    # ragged edges: whole blocks drop out, more so near the tip
                p_drop = .12 + .45 * t
                if rnd.random() < p_drop: x0 += 1
                if rnd.random() < p_drop: x1 -= 1
            d.line([(x0, yy), (x1, yy)], fill=col + (255,))
            if layer == 0:
                tip_row = max(tip_row, yy)
    for _ in range(rnd.randint(2, 4)):                                    # detached single blocks past the tip
        sx = int(x / U + rnd.randint(-2, 2)); sy = tip_row + rnd.randint(2, 6)
        if (sy + 1) * U <= CELL - 8:
            d.point((sx, sy), fill=(cols[1] if rnd.random() < .6 else cols[0]) + (255,))


def halo(canvas_size, op, lens_px, col):
    """One very faint soft elliptical halo (low alpha) round the plume, from the mouth centre line down."""
    cx, cy, rx, ry = op
    m = Image.new("RGBA", (CELL, CELL))
    ImageDraw.Draw(m).ellipse((cx - rx * 1.5, cy - ry, cx + rx * 1.5, cy + lens_px * 1.1), fill=col + (20,))
    keep = Image.new("L", (CELL, CELL), 0)
    ImageDraw.Draw(keep).rectangle((0, int(cy), CELL, CELL), fill=255)
    m.putalpha(Image.fromarray(np.minimum(np.array(m.getchannel("A")), np.array(keep)), "L"))
    return m


def pilot_lights(mouths, col, strength):
    """Cell 1: each bell's mouth glows from inside (a small lit ellipse, hard steps), nothing leaves it yet."""
    im = Image.new("RGBA", (CELL, CELL))
    d = ImageDraw.Draw(im)
    for cx, cy, rx, ry in mouths:
        a = round(255 * strength)
        d.ellipse((cx - rx * .72, cy - ry * .6, cx + rx * .72, cy + ry * .6), fill=col + (a,))
        d.ellipse((cx - rx * .36, cy - ry * .3, cx + rx * .36, cy + ry * .3), fill=(255, 255, 255, a))
    return im


def engine_cell(hull_cell, mouths, cols, lens, wks, seed, ragged=0.0):
    """One drawing: hull, a faint halo, then each plume starting at its bell's mouth centre, over the hull."""
    rnd = random.Random(seed)
    c = hull_cell.copy()
    for op, L in zip(mouths, lens):
        if L > 0:
            c.alpha_composite(halo(CELL, op, L * K, cols[0]))
    for op, L, k in zip(mouths, lens, wks):
        if L <= 0:
            continue
        cx, cy, rx, ry = op
        fl = Image.new("RGBA", (CELL // U, CELL // U))
        flame(fl, cx, cy, L * K, 2 * rx * .9 * k, cols, rnd, ragged)
        c.alpha_composite(fl.resize((CELL, CELL), Image.Resampling.NEAREST))
    return c


def clean_damaged(name, hull_cell, mouths):
    """The painted damaged hull without its painted flames: under and round every bell mouth the pixels come from the bare hull (cell 0),
    which has the empty bell and nothing below it (this also clears the flames' faint halo)."""
    dm = Image.open(HERE / "masters" / "damaged" / f"{name}.png").convert("RGBA")
    for cx, cy, rx, ry in mouths:
        x0, x1, y0 = int(cx - rx * 1.9), int(cx + rx * 1.9), int(cy - ry * .6)
        dm.paste(hull_cell.crop((x0, y0, x1, CELL)), (x0, y0))
    return dm


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
    cells = [hull_cell.copy()]                                                                       # 0 parked, engines off
    c1 = hull_cell.copy(); c1.alpha_composite(pilot_lights(mouths, cols[1], .85)); cells.append(c1)  # 1 pilot lights
    cells.append(engine_cell(hull_cell, mouths, cols, [12] * n, [.75] * n, 1))                       # 2 ignition
    for f in range(BURN):                                                                            # 3..10 burn cycle
        gs = [(f + 3 * i) % BURN for i in range(n)]
        cells.append(engine_cell(hull_cell, mouths, cols, [LENGTHS[g] for g in gs], [WIDTHS[g] for g in gs], 10 + f))
    dhull = clean_damaged(name, hull_cell, mouths)                                                   # 11..16 damaged burn loop
    for f in range(DMG):
        gs = [(f + 2 * i) % DMG for i in range(n)]
        lens = [DLENGTHS[g] if (f + i) % 5 != 3 else 5 for i, g in enumerate(gs)]
        cells.append(engine_cell(dhull, mouths, cols, lens, [DWIDTHS[g] for g in gs], 50 + f, ragged=.55))
    # The main strip keeps 12 cells (4608 px: textures wider than 4096 are downscaled on import, which breaks the square-cell slicing and
    # exceeds old phones' limit); the other damaged drawings (12..16) are the companion strip space_elite_<name>_damaged.png that
    # EliteArt appends to the frames (cell 11 = damaged loop drawing 0, cells 12..16 = drawings 1..5).
    def save(path, cs):
        st = Image.new("RGBA", (CELL * len(cs), CELL))
        for i, c in enumerate(cs):
            st.alpha_composite(c, (i * CELL, 0))
        st.save(path)
    out_dir.mkdir(parents=True, exist_ok=True)
    save(out_dir / f"space_elite_{name}.png", cells[:12])
    save(out_dir / f"space_elite_{name}_damaged.png", cells[12:])
    print(name, json.dumps(dict(hull_w=w, hull_h=h, ox=ox, oy=oy, mouths=[tuple(round(v, 1) for v in m) for m in mouths], bbox_cell3=cells[3].getbbox())))


if __name__ == "__main__":
    args = sys.argv[1:]
    out = RES
    if "--out" in args:
        i = args.index("--out"); out = Path(args[i + 1]); del args[i:i + 2]
    for n in (args or [n for n, c in SHIPS.items() if (HERE / "masters" / "v2" / f"{c['src']}.png").exists()]):
        build(n, out)
