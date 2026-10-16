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
    "riptide": dict(w=240, h=290, nz=((.427, .95), (.571, .95)), mouths={0: (175, 286, 11, 5), 1: (210, 286, 11, 5)}, bell=11, flame=((60, 130, 255), (130, 225, 255), (255, 255, 255)), len=20, crack=(255, 176, 32)),
    "bathyscaphe": dict(src="bathyscaphe_orig_repaint", w=262, h=238, nz=((.332, .95), (.672, .95)), mouths={0: (148, 266, 13, 10), 1: (237, 266, 13, 10)}, bell=13, flame=((60, 170, 255), (150, 235, 255), (255, 255, 255)), len=16, crack=(255, 255, 255)),
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
    """The dark mouth of each engine bell on the bare hull cell: (cx, cy, rx, ry) in cell px (the nozzle exit).
    seeds = a point inside each bell; manual = {engine index: (cx, cy, rx, ry)} for engines that are not a dark bell (Bathyscaphe's lamp pods)."""
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
    """One engine plume leaving (x, y) downward, on a canvas of U-px blocks: core + sheath layers (outer, mid, core).
    A slight neck at the root that flares out and then tapers; per-frame edge breakup on the sheath; the root and the tip edges are dithered
    (checkerboard), a few spark pixels fly off past the tip."""
    d = ImageDraw.Draw(canvas)
    tip_row = 0
    for layer, (col, lk, wk) in enumerate(zip(cols, (1.0, .72, .42), (1.0, .66, .34))):
        L = length * lk / U
        for row in range(int(L) + 1):
            t = row / max(1, L)
            ramp = min(1.0, t / .28)
            hw = (width * wk / (2 * U)) * (.78 + .42 * ramp * (2 - ramp)) * (1 - t ** 1.7) ** .9
            if layer == 0 and row > L * .6 and ragged:
                hw *= 1 - ragged * rnd.random()
            hw = max(hw, .5 if row < L else 0)
            x0 = int(round(x / U - hw)); x1 = max(x0, int(round(x / U + hw - .01)))
            yy = int(y / U) + row
            if layer < 2 and row > 2 and x1 - x0 >= 3:                    # per-frame breakup of the sheath's edge
                x0 += rnd.choice((0, 0, 1)); x1 -= rnd.choice((0, 0, 1))
            dither = layer < 2 and (row < 3 or t > .72)
            for xx in range(x0, x1 + 1):
                if dither and (xx in (x0, x1)) and (xx + row) % 2:
                    continue
                d.point((xx, yy), fill=col + (255,))
            if layer == 0:
                tip_row = max(tip_row, yy)
    for _ in range(4):                                                    # sparks past the tip
        if rnd.random() < .7:
            sx = int(x / U + rnd.randint(-2, 2)); sy = tip_row + rnd.randint(1, 3)
            d.point((sx, sy), fill=cols[1] + (255,))


def mouth_mask(op):
    """Where a flame may be drawn for a mouth: inside the mouth ellipse, or anywhere at / below its centre line."""
    cx, cy, rx, ry = op
    m = Image.new("L", (CELL, CELL), 0)
    ImageDraw.Draw(m).rectangle((0, int(cy), CELL, CELL), fill=255)
    ImageDraw.Draw(m).ellipse((cx - rx, cy - ry, cx + rx, cy + ry), fill=255)
    return m


def ell(op, grow_x, grow_y, upper=False):
    cx, cy, rx, ry = op
    m = Image.new("L", (CELL, CELL), 0)
    ImageDraw.Draw(m).ellipse((cx - rx - grow_x, cy - ry - grow_y, cx + rx + grow_x, cy + ry + grow_y), fill=255)
    if upper:
        ImageDraw.Draw(m).rectangle((0, int(cy), CELL, CELL), fill=0)
    return m


def mul(a, b):
    return Image.fromarray(np.minimum(np.array(a), np.array(b)), "L")


def engine_fx(base, mouths, lengths, cols, seed, ragged=0.0):
    """The hull cell with every engine burning: emissive spill on the hull round each mouth, a soft baked halo, the flame (clipped to the
    mouth, root just inside it) and the bell's lip redrawn over the flame root so the plume emerges from behind the rim."""
    rnd = random.Random(seed)
    out = base.copy()
    solid = base.getchannel("A").point(lambda v: 255 if v == 255 else 0)
    for op, (L, k) in zip(mouths, lengths):                              # spill on the hull and halo
        cx, cy, rx, ry = op
        sp = Image.new("RGBA", (CELL, CELL), cols[1] + (0,))
        sp.putalpha(mul(mul(ell(op, 7, 5), solid), Image.new("L", (CELL, CELL), 58)))
        out.alpha_composite(sp)
        halo = Image.new("RGBA", (CELL, CELL))
        hd = ImageDraw.Draw(halo)
        hd.ellipse((cx - rx * 1.5, cy - ry, cx + rx * 1.5, cy + L * K * 1.15 * k), fill=cols[0] + (16,))
        hd.ellipse((cx - rx * 1.05, cy - ry, cx + rx * 1.05, cy + L * K * .8 * k), fill=cols[1] + (26,))
        halo.putalpha(mul(halo.getchannel("A"), mouth_mask((cx, cy, rx * 3, ry))))
        out.alpha_composite(halo)
    for op, (L, k) in zip(mouths, lengths):                              # the flame
        cx, cy, rx, ry = op
        c = Image.new("RGBA", (CELL // U, CELL // U))
        flame(c, cx, cy - ry * .5, L * K + ry * .5, 2 * rx * .88 * k, cols, rnd, ragged)
        c = c.resize((CELL, CELL), Image.Resampling.NEAREST)
        c.putalpha(mul(c.getchannel("A"), mouth_mask(op)))
        out.alpha_composite(c)
    for op in mouths:                                                    # the bell's lip over the flame root
        lip = base.copy()
        lip.putalpha(mul(mul(ell(op, 3, 3, upper=True), Image.fromarray(255 - np.array(ell(op, -1, -1, upper=False)), "L")), base.getchannel("A")))
        out.alpha_composite(lip)
    return out


def pilot_lights(mouths, col, strength):
    """Cell 1: each mouth glows from inside (a small lit ellipse, hard steps), nothing leaves it yet."""
    im = Image.new("RGBA", (CELL, CELL))
    d = ImageDraw.Draw(im)
    for cx, cy, rx, ry in mouths:
        a = round(255 * strength)
        d.ellipse((cx - rx * .72, cy - ry * .6, cx + rx * .72, cy + ry * .6), fill=col + (a,))
        d.ellipse((cx - rx * .36, cy - ry * .3, cx + rx * .36, cy + ry * .3), fill=(255, 255, 255, a))
    return im


def _glow(hull_cell, mouths, cols):
    c = hull_cell.copy()
    c.alpha_composite(pilot_lights(mouths, cols[1], .85))
    return c


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
    seeds = [(ox + round(fx * w), oy + round(fy * h) - 14) for fx, fy in cfg["nz"]]
    mouths = find_openings(hull_cell, seeds, cfg.get("mouths", {}))
    cols, ln = cfg["flame"], cfg["len"] * 1.4

    n = len(mouths)
    cells = [dim(hull_cell, .82),
             _glow(hull_cell, mouths, cols),
             engine_fx(hull_cell, mouths, [(ln * .45, 1.0)] * n, cols, 1),
             engine_fx(hull_cell, mouths, [(ln * (1 + .08 * (i % 2)), .96 + .08 * (i % 2)) for i in range(n)], cols, 2)]
    for sign in (-1, 1):
        b, dx = bank(hull_cell, sign, oy, h)
        m2 = [(CELL / 2 + (x - CELL / 2) * .97 + dx(y), y, rx * .97, ry) for x, y, rx, ry in mouths]
        cells.append(engine_fx(b, m2, [(ln * 1.1, 1.0)] * n, cols, 3 + sign))
    dmg = engine_fx(dim(hull_cell, .88), mouths, [(ln * (.55 + .25 * (i % 2)), .8) for i in range(n)], cols, 99, ragged=.5)
    cracks(dmg, (ox, oy, ox + w, oy + h), cfg["crack"])
    cells.append(dmg)
    strip = Image.new("RGBA", (CELL * len(cells), CELL))
    for i, c in enumerate(cells):
        strip.alpha_composite(c, (i * CELL, 0))
    out_dir.mkdir(parents=True, exist_ok=True)
    strip.save(out_dir / f"tide_elite_{name}.png")
    print(name, json.dumps(dict(hull_w=w, hull_h=h, ox=ox, oy=oy, mouths=[tuple(round(v, 1) for v in m) for m in mouths], bbox_cell3=cells[3].getbbox())))


if __name__ == "__main__":
    args = sys.argv[1:]
    out = RES
    if "--out" in args:
        i = args.index("--out"); out = Path(args[i + 1]); del args[i:i + 2]
    for nme in (args or [n for n in SHIPS if (HERE / "masters" / f"{SHIPS[n].get('src', n)}.png").exists()]):
        build(nme, out)
