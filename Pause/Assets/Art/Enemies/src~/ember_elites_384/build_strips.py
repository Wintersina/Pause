#!/usr/bin/env python3
"""Ember elites at 384 px (the HD elite format of Tide / Storm / Desert / Space), from Codex re-renders of the approved 192 px designs.

Inputs per ship (masters/, flat #00FF40 key, nose up, empty dark engine bells):
  <name>.png           the hull re-rendered at 1024 px from the current art (pose A)
  <name>_flightb.png   the same ship, second flight pose (wings / pods / shields / claws moved a little)     [optional: falls back to A]
  <name>_damaged.png   the same ship, badly damaged (scorch, cracked + hanging plates, smoke)               [optional: falls back to a scripted one]
All three are scaled by ONE factor and registered on ONE anchor (the master's hull top / centre), so the nose and the engine bells stay put.

10 square cells of 384 px (3840 x 384: under the importer's 4096 px cap, so nothing is downscaled):
  0 parked (engines off)   1 parked idle (pilot lights)   2 lift-off ignition (stub flames)
  3 flight pose A   4 flight pose B (a painted second pose; each engine on its own flame length, Tide-style hard-stepped plume)
  5 bank left   6 bank right (pose A sheared a little)   7 damaged (painted damage, flames low and ragged)

  python3 build_strips.py [--out <dir>] [name ...]     default out: Resources/Elites/Ember
"""
import sys, json, random
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
RES = HERE.parents[2] / "Resources" / "Elites" / "Ember"
CELL = 384
K = 2
U = 4
BURN = 2
import os
MASTERS = Path(os.environ.get("EMBER_MASTERS", HERE / "masters"))     # override for dry runs
LEGACY = json.load(open(HERE / "inputs" / "legacy_bbox.json"))      # cell-3 bbox of the 192 px strips (x0, y0, x1, y1)
ORANGE = ((255, 98, 24), (255, 190, 70), (255, 255, 255))
# per ship: seeds = a point inside each engine bell (384 cell px, from the old 192 nozzles x2), manual mouths {i: (cx, cy, rx, ry)} when the
# dark-pixel search can't isolate a bell, flame colours (outer, mid, core), crack colour for the scripted damage fallback
SHIPS = {
    "sunstoke": dict(seeds=((160, 285), (224, 285)), mouths={1: (223, 273, 11, 4.5)}, flame=ORANGE),
    "coalrunner": dict(seeds=((140, 280), (190, 284), (240, 280)), flame=ORANGE),
    "brass_vulture": dict(seeds=((192, 328),), flame=ORANGE),
    "kilnback": dict(seeds=((158, 316), (224, 316)), mouths={0: (162, 282, 14, 6), 1: (224, 282, 14, 6)}, flame=ORANGE),
    "ash_wraith": dict(seeds=((166, 258), (218, 258)), lk=1.3, flame=((30, 120, 255), (110, 225, 255), (255, 255, 255))),
    "cauterizer": dict(seeds=((132, 300), (248, 300)), flame=ORANGE),
}
LENGTHS = (30, 38, 34, 42)       # flame length per drawing, 192-cell px (scaled by K); shortened when the cell has no room
WIDTHS = (1.0, .9, .96, .84)
POSE = (0, 1)                                    # which hull pose each flight drawing uses (A = 0, B = 1)
MARGIN = 8


def key_out(path):
    img = Image.open(path).convert("RGB")
    if img.size != (1024, 1024):               # Codex sometimes returns 1254 px: the ship keeps its share of the canvas, so normalise
        img = img.resize((1024, 1024), Image.Resampling.LANCZOS)
    im = np.array(img).astype(float)
    r, g, b = im[..., 0], im[..., 1], im[..., 2]
    gn = g - np.maximum(r, b)
    a = np.clip(1 - (gn - 35) / 80, 0, 1)
    gs = np.where(a < 1, np.minimum(g, np.maximum(r, b)), g)
    return Image.fromarray(np.dstack([r, gs, b, a * 255]).astype(np.uint8), "RGBA")


def solid(art):
    a = art.getchannel("A").point(lambda v: 255 if v >= 128 else 0)
    art = art.copy(); art.putalpha(a)
    return art


def tight(art):
    return art.getchannel("A").point(lambda v: 255 if v > 128 else 0).getbbox()


def drop_specks(art, min_px=60):
    """Remove small detached alpha islands (keying fringe, stray pixels) but keep the main body and every large part."""
    from collections import deque
    a = np.array(art.getchannel("A")) > 128
    H, W = a.shape
    seen = np.zeros_like(a)
    arr = np.array(art)
    for sy, sx in zip(*np.nonzero(a)):
        if seen[sy, sx]:
            continue
        comp = [(sy, sx)]; seen[sy, sx] = True
        q = deque(comp)
        while q:
            y, x = q.popleft()
            for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                if 0 <= ny < H and 0 <= nx < W and a[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True; q.append((ny, nx)); comp.append((ny, nx))
        if len(comp) < min_px:
            for y, x in comp:
                arr[y, x, 3] = 0
    return Image.fromarray(arr, "RGBA")


def load_master(name):
    p = MASTERS / f"{name}.png"
    return drop_specks(key_out(p)) if p.exists() else None


def frame_of(name):
    """Scale + anchor from the master: hull width = 2x the legacy 192 px drawing (same world size), nose at 2x the legacy top, centred."""
    m = load_master(name)
    bb = tight(m)
    mw, mh = bb[2] - bb[0], bb[3] - bb[1]
    x0, y0, x1, y1 = LEGACY[name]
    s = (x1 - x0) * K / mw
    # leave room for the flames under the hull
    s = min(s, (CELL - 2 * MARGIN - 64) / mh)
    top = max(MARGIN, min(y0 * K, CELL - MARGIN - 64 - round(mh * s)))
    return dict(bb=bb, s=s, top=top, cx=((bb[0] + bb[2]) / 2))


def place(name, art, fr, shift=(0, 0), sf=1.0):
    """Scale `art` (a 1024 keyed image) by the ship's factor and put it on the anchor; returns a 384 RGBA cell."""
    s, bb = fr["s"] * sf, fr["bb"]
    big = art.resize((round(art.width * s), round(art.height * s)), Image.Resampling.LANCZOS)
    big = solid(big)
    cell = Image.new("RGBA", (CELL, CELL))
    px = round(CELL / 2 - fr["cx"] * s) + shift[0]
    py = round(fr["top"] - bb[1] * s) + shift[1]
    cell.paste(big, (px, py))          # onto an empty cell: copies RGBA as is, works with negative offsets
    return cell


def register(base, art, fr, search=8):
    """Best (scale factor, dx, dy) to lay a variant master `art` over pose A `base`: the central body (a band round the hull axis, where only
    small parts move) must match in colour and silhouette. Coarse-to-fine over scale 0.97..1.03 and a +-8 px window (Codex keeps the canvas placement; a wide search latches onto repeating panel patterns)."""
    A = np.array(base).astype(np.int32)
    aa = A[..., 3] > 128
    ys, xs = np.nonzero(aa)
    cx0 = int((xs.min() + xs.max()) // 2)
    x0, x1 = cx0 - 56, cx0 + 56
    y0, y1 = int(ys.min()), int(ys.max())
    Ab, aab = A[:, x0:x1], aa[:, x0:x1]

    def err(V, vv, dx, dy):
        Vs = np.roll(np.roll(V, dy, 0), dx, 1)[:, x0:x1]
        vs = np.roll(np.roll(vv, dy, 0), dx, 1)[:, x0:x1]
        inter = aab & vs
        return int(np.abs(Ab - Vs).sum(-1)[inter].sum()) + 300 * int((aab ^ vs).sum())

    best = (1e18, 1.0, 0, 0)
    cache = {}

    def vsf(sf):
        if sf not in cache:
            V = np.array(place("", art, fr, (0, 0), sf)).astype(np.int32)
            cache[sf] = (V, V[..., 3] > 128)
        return cache[sf]

    for sf in [0.97, 0.98, 0.99, 1.0, 1.01, 1.02, 1.03]:
        V, vv = vsf(sf)
        for dy in range(-search, search + 1, 4):
            for dx in range(-search, search + 1, 4):
                e = err(V, vv, dx, dy)
                if e < best[0]:
                    best = (e, sf, dx, dy)
    _, sf0, dx0, dy0 = best
    for sf in [round(sf0 + .005 * i, 3) for i in range(-4, 5)]:
        V, vv = vsf(sf)
        for dy in range(dy0 - 4, dy0 + 5):
            for dx in range(dx0 - 4, dx0 + 5):
                e = err(V, vv, dx, dy)
                if e < best[0]:
                    best = (e, sf, dx, dy)
    return best[1], best[2], best[3]


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
    """The dark mouth of each engine bell on the bare hull cell: (cx, cy, rx, ry) in cell px (Tide / Space recipe)."""
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
    """One engine plume leaving (x, y) downward on a canvas of U-px blocks: white core, mid body, darker outer layer, all hard-stepped;
    it widens just below the exit then tapers raggedly (edge blocks drop out, more towards the tip, different every drawing) and ends in a few
    detached single blocks. (The Tide / Space recipe, docs/engine-flame-art-recipe.md.)"""
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
            if layer < 2 and row > 1 and x1 - x0 >= 2:
                p_drop = .12 + .45 * t
                if rnd.random() < p_drop: x0 += 1
                if rnd.random() < p_drop: x1 -= 1
            d.line([(x0, yy), (x1, yy)], fill=col + (255,))
            if layer == 0:
                tip_row = max(tip_row, yy)
    for _ in range(rnd.randint(2, 4)):
        sx = int(x / U + rnd.randint(-2, 2)); sy = tip_row + rnd.randint(2, 6)
        if (sy + 1) * U <= CELL - MARGIN:
            d.point((sx, sy), fill=(cols[1] if rnd.random() < .6 else cols[0]) + (255,))


def halo(op, lens_px, col):
    cx, cy, rx, ry = op
    m = Image.new("RGBA", (CELL, CELL))
    ImageDraw.Draw(m).ellipse((cx - rx * 1.5, cy - ry, cx + rx * 1.5, cy + lens_px * 1.1), fill=col + (20,))
    keep = Image.new("L", (CELL, CELL), 0)
    ImageDraw.Draw(keep).rectangle((0, int(cy), CELL, CELL), fill=255)
    m.putalpha(Image.fromarray(np.minimum(np.array(m.getchannel("A")), np.array(keep)), "L"))
    return m


def pilot_lights(mouths, col, strength):
    im = Image.new("RGBA", (CELL, CELL))
    d = ImageDraw.Draw(im)
    for cx, cy, rx, ry in mouths:
        a = round(255 * strength)
        d.ellipse((cx - rx * .72, cy - ry * .6, cx + rx * .72, cy + ry * .6), fill=col + (a,))
        d.ellipse((cx - rx * .36, cy - ry * .3, cx + rx * .36, cy + ry * .3), fill=(255, 255, 255, a))
    return im


def engine_cell(hull_cell, mouths, cols, lens, wks, seed, ragged=0.0, room=None):
    """hull + faint halo + each plume starting at its bell's mouth centre, over the hull. `room` caps the plume so it stays inside the cell."""
    rnd = random.Random(seed)
    c = hull_cell.copy()
    ls = []
    for (cx, cy, rx, ry), L in zip(mouths, lens):
        ls.append(min(L * K, max(0, CELL - MARGIN - 8 - cy)) / K)
    for op, L in zip(mouths, ls):
        if L > 0:
            c.alpha_composite(halo(op, L * K, cols[0]))
    for op, L, k in zip(mouths, ls, wks):
        if L <= 0:
            continue
        cx, cy, rx, ry = op
        fl = Image.new("RGBA", (CELL // U, CELL // U))
        flame(fl, cx, cy, L * K, 2 * rx * .9 * k, cols, rnd, ragged)
        c.alpha_composite(fl.resize((CELL, CELL), Image.Resampling.NEAREST))
    return c


def bank(cell, sign, top, h):
    a = np.asarray(cell)
    yy, xx = np.indices((CELL, CELL))
    mid = top + h * .55
    shift = np.rint(sign * 4 * (mid - yy) / (h * .5)).astype(int)
    src = np.clip(np.rint(CELL / 2 + (xx - shift - CELL / 2) / .97).astype(int), 0, CELL - 1)
    return Image.fromarray(a[yy, src], "RGBA"), (lambda y: int(round(sign * 4 * (mid - y) / (h * .5))))


def scripted_damage(hull_cell, bbox):
    """Fallback only (no painted damaged master yet): dimmed hull + dark cracks with a hot core, no pink."""
    a = np.asarray(hull_cell).astype(np.float32).copy()
    s = a[..., 3] > 0
    a[s, :3] *= .8
    im = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")
    d = ImageDraw.Draw(im)
    x0, y0, x1, y1 = bbox
    cx = (x0 + x1) // 2
    cy = y0 + (y1 - y0) // 3
    d.line([(cx - 4, cy - 28), (cx + 12, cy - 10), (cx, cy + 6), (cx + 18, cy + 26)], fill=(10, 14, 24, 255), width=7)
    d.line([(cx - 4, cy - 28), (cx + 12, cy - 10), (cx, cy + 6), (cx + 18, cy + 26)], fill=(255, 150, 40, 255), width=3)
    return im


def hulls(name):
    """(frame, poseA cell, poseB cell, damaged cell, painted_damage, painted_b) -- all registered to pose A."""
    fr = frame_of(name)
    A = place(name, load_master(name), fr)
    outs = []
    for suffix in ("flightb", "damaged"):
        p = MASTERS / f"{name}_{suffix}.png"
        if p.exists():
            art = drop_specks(key_out(p))
            sf, dx, dy = register(A, art, fr)
            print("  registered", name, suffix, "scale", round(sf, 3), "shift", dx, dy)
            outs.append(place(name, art, fr, (dx, dy), sf))
        else:
            outs.append(None)
    return fr, A, outs[0], outs[1]


def build(name, out_dir):
    cfg = SHIPS[name]
    fr, A, B, Dm = hulls(name)
    bb = A.getchannel("A").point(lambda v: 255 if v > 128 else 0).getbbox()
    mouths = find_openings(A, cfg["seeds"], cfg.get("mouths", {}))
    cols = cfg["flame"]
    lk = cfg.get("lk", 1.0)          # flame length factor (Ash Wraith's tail blades hang far below its bells)
    n = len(mouths)
    Bh = B or A
    cells = [A.copy()]                                                                       # 0 parked, engines off
    c1 = A.copy(); c1.alpha_composite(pilot_lights(mouths, cols[1], .85)); cells.append(c1)   # 1 pilot lights
    cells.append(engine_cell(A, mouths, cols, [12] * n, [.75] * n, 1))                         # 2 ignition
    for f in range(BURN):                                                                    # 3, 4 flight loop
        gs = [(2 * f + i) % 4 for i in range(n)]
        cells.append(engine_cell(Bh if POSE[f] else A, mouths, cols, [LENGTHS[g] * lk for g in gs], [WIDTHS[g] for g in gs], 10 + f))
    for sign in (-1, 1):                                                                     # 5, 6 banks
        b, dx = bank(A, sign, bb[1], bb[3] - bb[1])
        m2 = [(CELL / 2 + (x - CELL / 2) * .97 + dx(y), y, rx * .97, ry) for x, y, rx, ry in mouths]
        cells.append(engine_cell(b, m2, cols, [LENGTHS[(i + 1) % 4] * 1.05 for i in range(n)], [.96] * n, 20 + sign))
    dh = Dm if Dm is not None else scripted_damage(A, bb)
    cells.append(engine_cell(dh, mouths, cols, [18 + 6 * (i % 2) for i in range(n)], [.8] * n, 99, ragged=.5))   # 7 damaged
    strip = Image.new("RGBA", (CELL * len(cells), CELL))
    for i, c in enumerate(cells):
        strip.alpha_composite(c, (i * CELL, 0))
    out_dir.mkdir(parents=True, exist_ok=True)
    strip.save(out_dir / f"ember_elite_{name}.png")
    print(name, json.dumps(dict(s=round(fr["s"], 4), top=fr["top"], bbox_hull=bb, painted_b=B is not None, painted_dmg=Dm is not None,
                                mouths=[tuple(round(v, 1) for v in m) for m in mouths], bbox_cell3=cells[3].getbbox())))


if __name__ == "__main__":
    args = sys.argv[1:]
    out = RES
    if "--out" in args:
        i = args.index("--out"); out = Path(args[i + 1]); del args[i:i + 2]
    for nme in (args or [n for n in SHIPS if (MASTERS / f"{n}.png").exists()]):
        build(nme, out)
