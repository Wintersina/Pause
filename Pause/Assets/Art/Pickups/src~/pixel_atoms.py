#!/usr/bin/env python3
"""Pixel-art pickup family: the green heal atom's siblings and every atom's
idle / pickup flipbooks.

The green heal atom (Art/Resources/Pickups/heal_atom_green.png) is the
benchmark and is never redrawn: its idle animation is a flipbook of
*overlay* frames drawn on top of the untouched original (electron glints in
sequence, then a nucleus pulse). Its siblings are drawn here in the same
manner: a coarse cell grid, crisp cells only (no anti-aliasing), a dark ink
outline that thickens to the lower right, balls shaded with a dark, a mid, a
light tone and a hard white kick. Every grid is then upscaled with
nearest-neighbour, so the sprites stay crisp under Point filtering.

  python3 pixel_atoms.py            # writes every PNG the game loads
  python3 pixel_atoms.py --preview  # also writes strips / GIFs / sheets to
                                    # docs/art-samples/atoms/

Cell density matches the green atom (about 47 cells across its 0.28 world
units): a 49-cell grid, x4, at PPU 700 is exactly 0.28 u.

Frame tables (hold ticks at 24 fps) must match Scripts/Gameplay/PickupArt.cs;
ArtRestyleTest checks that every frame file exists.
"""
import math
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
OUT_FRAMES = os.path.join(ASSETS, "Art", "Resources", "Pickups", "Atoms")
REPO = os.path.normpath(os.path.join(ASSETS, "..", ".."))
PREVIEW = os.path.join(REPO, "docs", "art-samples", "atoms")


def hexc(h):
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), 255)


# --------------------------------------------------------------- palette ---
INK = hexc("#140C14")
BONE = hexc("#F4EAD4")

# Each ramp: shadow, base, light, kick (white). Guide colours only.
RAMPS = {
    "shield": [hexc("#0F5E6A"), hexc("#1FB5B9"), hexc("#6EF2EE"), BONE],   # TEAL_SH TEAL CYAN
    "pause": [hexc("#86121F"), hexc("#D8232C"), hexc("#FF5B45"), BONE],    # RED_SH RED RED_HI
    "dust": [hexc("#A9481A"), hexc("#F2862B"), hexc("#FFB43C"), BONE],     # SODIUM_SH SODIUM AMBER
    # The green atom's own tones, sampled from heal_atom_green.png.
    "heal": [hexc("#29A805"), hexc("#7EE702"), hexc("#B4F246"), hexc("#FDFDFD")],
}
HEAL_INK = hexc("#00021B")

SH, BASE, LIGHT, KICK = 1, 2, 3, 4      # cell codes; 5 = ink, 0 = empty
INKC = 5

UP = 4                                   # nearest-neighbour upscale
ATOM_CELLS = 49                          # 49 * 4 = 196 px; PPU 700 -> 0.28 u
BURST_CELLS = 97                         # 388 px; PPU 700 -> 0.554 u
DUST_CELLS = 45                          # 180 px over 2.56 local u (prefab scale 0.1)
DUST_SM_CELLS = 11                       # 44 px over 1.28 local u (prefab scale 0.05)


# ------------------------------------------------------------ grid tools ---
class Grid:
    def __init__(self, n, m=None):
        self.a = np.zeros((m or n, n), dtype=np.uint8)

    @property
    def h(self):
        return self.a.shape[0]

    @property
    def w(self):
        return self.a.shape[1]

    def cells(self):
        ys, xs = np.mgrid[0:self.h, 0:self.w]
        return xs + 0.5, ys + 0.5

    def set(self, mask, code):
        self.a[mask] = code


def outline(layer, drop=True):
    """Ink ring around a layer's filled cells: 8-neighbour, plus one extra
    cell to the lower right (light comes from the upper left)."""
    filled = layer > 0
    ink = np.zeros_like(filled)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            ink |= shift(filled, dx, dy)
    if drop:
        ink |= shift(filled, 1, 2) | shift(filled, 2, 1) | shift(filled, 2, 2)
    out = layer.copy()
    out[ink & ~filled] = INKC
    return out


def shift(m, dx, dy):
    out = np.zeros_like(m)
    h, w = m.shape
    ys = slice(max(0, dy), min(h, h + dy))
    yd = slice(max(0, -dy), min(h, h - dy))
    xs = slice(max(0, dx), min(w, w + dx))
    xd = slice(max(0, -dx), min(w, w - dx))
    out[ys, xs] = m[yd, xd]
    return out


def over(dst, src):
    m = src > 0
    dst[m] = src[m]
    return dst


def ball(g, cx, cy, r, flash=0, kick=True):
    """Shaded pixel ball: base, a lower-right shadow crescent, an upper-left
    light band and a hard square white kick."""
    x, y = g.cells()
    dx, dy = x - cx, y - cy
    inside = dx * dx + dy * dy <= r * r + r * 0.6
    d = (dx + dy) * 0.7071 / max(r, 0.5)
    g.set(inside, BASE)
    g.set(inside & (d > 0.38), SH)
    g.set(inside & (d < -0.42), LIGHT)
    if flash:
        g.set(inside & (g.a == SH), BASE)
        g.set(inside & (g.a == BASE) & (d < 0.2), LIGHT)
    if kick and r >= 2.5:
        k = max(1.0, r * 0.32)
        kx, ky = cx - r * 0.42, cy - r * 0.42
        g.set(inside & (np.abs(x - kx) <= k * 0.75) & (np.abs(y - ky) <= k * 0.75), KICK)


def ellipse_ring(g, cx, cy, rx, ry, rot, thick, code_out=LIGHT, code_in=BASE, keep=None):
    x, y = g.cells()
    c, s = math.cos(math.radians(rot)), math.sin(math.radians(rot))
    u = (x - cx) * c + (y - cy) * s
    v = -(x - cx) * s + (y - cy) * c
    e = np.sqrt((u / rx) ** 2 + (v / ry) ** 2) + 1e-6
    grad = np.sqrt((u / rx ** 2) ** 2 + (v / ry ** 2) ** 2) / e + 1e-6
    dist = (e - 1) / grad
    ring = np.abs(dist) <= thick / 2
    if keep is not None:
        ring &= keep(u, v)
    g.set(ring & (dist < 0), code_in)
    g.set(ring & (dist >= 0), code_out)
    return ring


def ellipse_point(cx, cy, rx, ry, rot, t):
    c, s = math.cos(math.radians(rot)), math.sin(math.radians(rot))
    u, v = rx * math.cos(t), ry * math.sin(t)
    return cx + u * c - v * s, cy + u * s + v * c


def seg_dist(px, py, ax, ay, bx, by):
    vx, vy = bx - ax, by - ay
    t = np.clip(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy), 0, 1)
    return np.hypot(px - (ax + t * vx), py - (ay + t * vy))


def poly_ring(g, pts, thick, code_out=LIGHT, code_in=BASE, cx=None, cy=None):
    x, y = g.cells()
    d = np.full(x.shape, 1e9)
    for i in range(len(pts)):
        ax, ay = pts[i]
        bx, by = pts[(i + 1) % len(pts)]
        d = np.minimum(d, seg_dist(x, y, ax, ay, bx, by))
    ring = d <= thick / 2
    if cx is not None:
        # outer half of the band lighter, like the green atom's orbits
        rr = np.hypot(x - cx, y - cy)
        rmid = np.mean([math.hypot(px - cx, py - cy) for px, py in pts]) * 0.93
        g.set(ring, code_in)
        g.set(ring & (y - cy < -(x - cx) * 0.2) , code_out)
    else:
        g.set(ring, code_in)
    return ring


def poly_fill(g, pts):
    x, y = g.cells()
    inside = np.zeros(x.shape, dtype=bool)
    n = len(pts)
    j = n - 1
    for i in range(n):
        xi, yi = pts[i]
        xj, yj = pts[j]
        cond = ((yi > y) != (yj > y)) & (x < (xj - xi) * (y - yi) / (yj - yi + 1e-9) + xi)
        inside ^= cond
        j = i
    return inside


def to_image(cells, ramp, ink=INK, up=UP):
    lut = np.zeros((6, 4), dtype=np.uint8)
    for i, c in enumerate(ramp):
        lut[i + 1] = c
    lut[INKC] = ink
    rgba = lut[cells]
    img = Image.fromarray(rgba, "RGBA")
    return img.resize((img.width * up, img.height * up), Image.NEAREST)


# ------------------------------------------------------------- the atoms ---
def shield_frame(phase, pulse):
    """Shield atom: a hexagonal orbit cage around a split heater-shield
    nucleus, three cube electrons stepping round the hexagon."""
    n = ATOM_CELLS
    c = n / 2
    out = np.zeros((n, n), dtype=np.uint8)

    # hexagon orbit (pointy top), behind everything
    hexpts = [(c + 19.5 * math.cos(math.radians(-90 + 60 * k)),
               c + 19.5 * math.sin(math.radians(-90 + 60 * k))) for k in range(6)]
    g = Grid(n)
    poly_ring(g, hexpts, 2.2, LIGHT, BASE, c, c)
    # inner tilted ring for depth
    g2 = Grid(n)
    ellipse_ring(g2, c, c, 13.5, 6.0, -20, 1.6, BASE, SH)
    over(out, outline(g2.a, drop=False))
    over(out, outline(g.a))

    # nucleus: heater shield, split light/base down the middle
    s = {-1: 0.86, 0: 1.0, 1: 1.12, 2: 1.04}[pulse]
    sx, sy = 9.5 * s, 11.5 * (2 - s if pulse == -1 else s)
    shield = [(c - sx, c - sy), (c + sx, c - sy), (c + sx, c + sy * 0.15),
              (c, c + sy * 1.05), (c - sx, c + sy * 0.15)]
    shield = [(px, py + 0.5) for px, py in shield]
    g = Grid(n)
    x, y = g.cells()
    inside = poly_fill(g, shield)
    g.set(inside, BASE)
    g.set(inside & (x < c), LIGHT if pulse != -1 else BASE)
    g.set(inside & (x >= c) & ((y - c) > (x - c) * 0.9 + sy * 0.05), SH)
    # chevron emblem (BONE kick) - flashes wider on the pop
    chev_w = 1.6 if pulse != 1 else 2.2
    chev = (np.abs((y - (c + 2.5)) + np.abs(x - c) * 0.95) <= chev_w) & (np.abs(x - c) <= sx * 0.6)
    g.set(inside & chev, KICK)
    # top band
    g.set(inside & (y < c - sy + 2.2), SH if pulse == -1 else LIGHT)
    over(out, outline(g.a))

    # electrons: three cubes stepping round the hexagon
    for k in range(3):
        t = (phase + k / 3.0) % 1.0
        seg = t * 6
        i = int(seg)
        f = seg - i
        ax, ay = hexpts[i % 6]
        bx, by = hexpts[(i + 1) % 6]
        ex, ey = ax + (bx - ax) * f, ay + (by - ay) * f
        g = Grid(n)
        x, y = g.cells()
        r = 3.6
        cube = np.abs(x - ex) + np.abs(y - ey) <= r + 0.4
        g.set(cube, BASE)
        g.set(cube & ((x - ex) + (y - ey) * 0.4 < -0.6), LIGHT)
        g.set(cube & ((x - ex) * 0.4 + (y - ey) > 1.2), SH)
        g.set(cube & (np.abs(x - (ex - 1)) <= 0.6) & (np.abs(y - (ey - 1)) <= 0.6), KICK)
        over(out, outline(g.a))
    return out


def pause_frame(phase, pulse):
    """Pause atom: two orbits crossed in an X around a red ball whose BONE
    pause bars blink on the beat; two electrons chase round the orbits."""
    n = ATOM_CELLS
    c = n / 2
    out = np.zeros((n, n), dtype=np.uint8)
    rx, ry, tilt = 17.6, 6.6, 34
    for rot in (tilt, -tilt):
        g = Grid(n)
        ellipse_ring(g, c, c, rx, ry, rot, 2.0, LIGHT, BASE)
        over(out, outline(g.a))

    # electrons: both chase the same way round, half a turn apart; on the
    # far half of their orbit they pass behind the nucleus
    front, back = [], []
    for k, rot in enumerate((tilt, -tilt)):
        t = 2 * math.pi * (phase + 0.5 * k)
        ex, ey = ellipse_point(c, c, rx, ry, rot, t)
        g = Grid(n)
        ball(g, ex, ey, 3.5)
        (back if math.sin(t) < -0.25 else front).append(outline(g.a))
    for lay in back:
        over(out, lay)
    r = {-1: 8.2, 0: 9.2, 1: 10.3, 2: 9.6}[pulse]
    g = Grid(n)
    ball(g, c, c, r, flash=(pulse == 1), kick=False)
    x, y = g.cells()
    # octagonal cut: chamfer the ball into the guide's angular language
    cut = (np.abs(x - c) + np.abs(y - c)) > r * 1.32
    g.a[cut] = 0
    # pause bars
    bh = {-1: 3.0, 0: 4.6, 1: 5.4, 2: 4.6}[pulse]
    for bxc in (c - 2.6, c + 2.6):
        bar = (np.abs(x - bxc) <= 1.1) & (np.abs(y - c) <= bh)
        g.set(bar, KICK)
    lay = outline(g.a)
    # ink inside around the bars so they read at phone size
    bars = g.a == KICK
    ring = np.zeros_like(bars)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            ring |= shift(bars, dx, dy)
    lay[ring & ~bars & (g.a > 0) & (np.abs(x - c) > 1.5)] = INKC
    # a hard kick at the upper left of the ball
    kk = (np.abs(x - (c - r * 0.55)) <= 0.8) & (np.abs(y - (c - r * 0.55)) <= 0.8) & (g.a > 0)
    lay[kk] = KICK
    over(out, lay)
    for lay in front:
        over(out, lay)

    return out


def star_cells(n, rx, ry, w, flip, sparkle=0, scale=1.0, core=True):
    """Four-point star, cel-faceted: each ray split along its axis into a
    light and a shadow half (light from the upper left), BONE core."""
    c = n / 2
    g = Grid(n)
    x, y = g.cells()
    dx, dy = x - c, y - c
    pts = []
    for k in range(8):
        a = math.radians(-90 + 45 * k)
        if k % 2 == 0:
            rr = ry if k % 4 == 0 else rx
            pts.append((c + rr * math.cos(a), c + rr * math.sin(a)))
        else:
            pts.append((c + w * math.cos(a), c + w * math.sin(a)))
    inside = poly_fill(g, pts)
    g.set(inside, BASE)
    lightside = (dx + dy) < 0 if not flip else (dx + dy) > 0
    shadeside = (dx - dy) > 0 if not flip else (dx - dy) < 0
    g.set(inside & lightside & ~shadeside, LIGHT)
    g.set(inside & shadeside & ~lightside, SH)
    if core:
        cr = max(0.8, w * 0.42)
        g.set(inside & (np.abs(dx) + np.abs(dy) <= cr + 0.3), KICK)
    out = outline(g.a, drop=n > 20)
    if sparkle:
        # tiny satellite twinkles at the diagonals (BONE plus with ink)
        for k in range(4):
            if (sparkle >> k) & 1 == 0:
                continue
            a = math.radians(-45 + 90 * k)
            sx, sy = c + n * 0.36 * math.cos(a), c + n * 0.36 * math.sin(a)
            s = Grid(n)
            px, py = s.cells()
            plus = ((np.abs(px - sx) <= 0.6) & (np.abs(py - sy) <= 1.6)) | \
                   ((np.abs(py - sy) <= 0.6) & (np.abs(px - sx) <= 1.6))
            s.set(plus, KICK)
            s.set(plus & (np.abs(px - sx) <= 0.6) & (np.abs(py - sy) <= 0.6), LIGHT)
            over(out, outline(s.a, drop=False))
    return out


# Frame tables: (params, hold ticks). Keep in step with PickupArt.cs.
# rest pose, ten travelling drawings on 2s, then anticipation, pop, settle
ATOM_IDLE_TICKS = [6, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3]
ATOM_PULSE = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, 1, 2]

DUST_IDLE = [  # rx, ry, w, flip, sparkle bits, ticks
    (19, 19, 6.0, 0, 0, 8),
    (15, 15, 7.5, 0, 0, 2),     # anticipation: squash in
    (21.5, 21.5, 4.8, 0, 0b0101, 2),   # stretch out, twinkles
    (11, 21, 4.2, 0, 0b1010, 1),
    (2.5, 21.5, 1.6, 0, 0, 1),         # smear: edge-on
    (11, 21, 4.2, 1, 0, 1),
    (19, 19, 6.0, 1, 0, 4),            # held on the flip side
    (11, 21, 4.2, 1, 0, 1),
    (2.5, 21.5, 1.6, 1, 0, 1),
    (11, 21, 4.2, 0, 0, 1),
]

HEAL_IDLE_TICKS = [8, 2, 1, 2, 1, 2, 1, 2, 2, 2, 3]
BURST_TICKS = [1, 2, 2, 2, 2, 3]


def atom_idle(kind):
    frames = []
    for i, ticks in enumerate(ATOM_IDLE_TICKS):
        pulse = ATOM_PULSE[i]
        if kind == "shield":
            # electrons step one hexagon edge per two frames; the loop closes
            # after 1/3 turn because the three electrons are identical
            phase = (i / len(ATOM_IDLE_TICKS)) / 3.0
            frames.append((shield_frame(phase, pulse), ticks))
        else:
            # two different orbits: a whole turn per loop
            phase = i / len(ATOM_IDLE_TICKS)
            frames.append((pause_frame(phase, pulse), ticks))
    return frames


def dust_idle(n):
    k = n / DUST_CELLS
    out = []
    for rx, ry, w, flip, sp, ticks in DUST_IDLE:
        if n < 20:
            # the small grain: shapes simplified so they survive at 11 cells
            out.append((star_cells(n, max(0.8, rx * k), ry * k, max(1.0, w * k), flip, 0), ticks))
        else:
            out.append((star_cells(n, rx * k, ry * k, w * k, flip, sp), ticks))
    return out


# -------------------------------------------------------------- bursts ---
def burst(kind):
    """Pickup burst: impact flash, inked burst star, then a ring that breaks
    into shards and per-kind glyph confetti. Drawn white-hot first frame."""
    n = BURST_CELLS
    c = n / 2
    frames = []
    shapes = {"heal": "plus", "pause": "bars", "shield": "hex", "dust": "star"}[kind]
    for fi, ticks in enumerate(BURST_TICKS):
        out = np.zeros((n, n), dtype=np.uint8)
        g = Grid(n)
        x, y = g.cells()
        dx, dy = x - c, y - c
        r = np.hypot(dx, dy)
        ang = np.arctan2(dy, dx)
        if fi == 0:
            # impact: flat white four-point flash
            out = over(out, star_cells(n, 26, 26, 6, 0, 0, core=False))
            out[(out > 0) & (out < INKC)] = KICK
        elif fi == 1:
            # 8-point burst star, light with a kick core
            spikes = 22 + 8 * np.cos(ang * 8) ** 8
            body = r <= spikes * 0.95
            g.set(body, LIGHT)
            g.set(body & (r <= 9), KICK)
            g.set(body & (r > 9) & ((dx + dy) > 6), BASE)
            over(out, outline(g.a, drop=False))
        else:
            k = fi - 2           # 0..3
            ring_r = [20, 27, 33, 38][k]
            thick = [3.2, 2.4, 1.6, 1.0][k]
            band = np.abs(r - ring_r) <= thick / 2
            if k >= 1:
                # break the ring into dashes, fewer as it spreads
                dash = (np.cos(ang * (8 if k < 3 else 12)) > [0, -0.2, 0.3, 0.6][k])
                band &= dash
            g.set(band, LIGHT if k < 2 else BASE)
            if k == 0:
                g.set(r <= 5 - 0, KICK)
            over(out, outline(g.a, drop=False) if k < 3 else g.a)
            # glyph confetti flying out on the diagonals
            if k < 3:
                for j in range(4):
                    a = math.radians(45 + 90 * j + k * 10)
                    dist = [24, 32, 40][k]
                    gx, gy = c + dist * math.cos(a), c + dist * math.sin(a)
                    s = Grid(n)
                    px, py = s.cells()
                    sz = [2.6, 2.0, 1.4][k]
                    if shapes == "plus":
                        m = ((np.abs(px - gx) <= sz * 0.45) & (np.abs(py - gy) <= sz * 1.3)) | \
                            ((np.abs(py - gy) <= sz * 0.45) & (np.abs(px - gx) <= sz * 1.3))
                    elif shapes == "bars":
                        m = ((np.abs(px - gx - sz * 0.75) <= sz * 0.35) | (np.abs(px - gx + sz * 0.75) <= sz * 0.35)) & \
                            (np.abs(py - gy) <= sz * 1.2)
                    elif shapes == "hex":
                        m = (np.abs(px - gx) + np.abs(py - gy) <= sz * 1.3)
                    else:
                        m = ((np.abs(px - gx) <= sz * 0.4) & (np.abs(py - gy) <= sz * 1.6)) | \
                            ((np.abs(py - gy) <= sz * 0.4) & (np.abs(px - gx) <= sz * 1.6))
                    s.set(m, KICK if k == 0 else LIGHT)
                    over(out, outline(s.a, drop=False))
            else:
                # last frame: a few single sparkle cells
                for j in range(8):
                    a = math.radians(22.5 + 45 * j)
                    gx, gy = c + 44 * math.cos(a), c + 44 * math.sin(a)
                    out[int(gy), int(gx)] = KICK if j % 2 else LIGHT
        frames.append((out, ticks))
    return frames


# --------------------------------------------- heal: overlays on the original
# The green atom's sprite is a 944 px square crop of the 1254 px original
# (HealAtom.ArtRect). Overlays are drawn on a 47-cell grid covering that same
# square (5 px per cell, 235 px), roughly the original's own pixel grid.
HEAL_CELLS = 47
HEAL_UP = 5
HEAL_CROP = (155.0, 147.5, 944.0)   # x0, y0 (from the top), side, in original px


def heal_cell(px, py):
    x0, y0, side = HEAL_CROP
    return (px - x0) / side * HEAL_CELLS, (py - y0) / side * HEAL_CELLS


# Feature centres measured on the original (pixels, origin top-left).
HEAL_NUCLEUS = heal_cell(616, 637)
HEAL_NUCLEUS_R = 95 / 944 * HEAL_CELLS
HEAL_ELECTRONS = [heal_cell(619, 232), heal_cell(929, 830), heal_cell(308, 830)]  # top, right, left
HEAL_ELECTRON_R = 62 / 944 * HEAL_CELLS


def lit_ball(n, cx, cy, r, twinkle=0, kick=True):
    """An electron (or the nucleus) lighting up: its disc redrawn one step
    brighter, a fat white kick, and optionally a four-point twinkle on its
    upper-left. No ink: these are lights laid over the original's own."""
    g = Grid(n)
    x, y = g.cells()
    dx, dy = x - cx, y - cy
    inside = dx * dx + dy * dy <= r * r + r * 0.5
    d = (dx + dy) * 0.7071 / max(r, 0.5)
    g.set(inside & (d > 0.45), BASE)
    g.set(inside & (d <= 0.45), LIGHT)
    k = max(1.0, r * 0.36)
    kx, ky = cx - r * 0.4, cy - r * 0.4
    if kick:
        g.set(inside & (np.abs(x - kx) <= k) & (np.abs(y - ky) <= k), KICK)
    if twinkle:
        tx, ty = cx - r * 0.75, cy - r * 0.75
        ax, ay = np.abs(x - tx), np.abs(y - ty)
        arm = (((ax <= 0.5) & (ay <= twinkle)) | ((ay <= 0.5) & (ax <= twinkle)))
        g.set(arm, KICK)
    return g.a


def streak(n, a, b, width=1.0):
    """1-tick smear: a light travelling along the orbit, hot at its head."""
    g = Grid(n)
    x, y = g.cells()
    d = seg_dist(x, y, a[0], a[1], b[0], b[1])
    t = np.clip(((x - a[0]) * (b[0] - a[0]) + (y - a[1]) * (b[1] - a[1])) /
                ((b[0] - a[0]) ** 2 + (b[1] - a[1]) ** 2), 0, 1)
    band = d <= width * 0.6
    g.set(band & (t > 0.7), KICK)
    g.set(band & (t <= 0.7), LIGHT)
    return g.a


def nucleus_ring(n, radius, dashes=0, thick=1.0, code=LIGHT):
    g = Grid(n)
    x, y = g.cells()
    cx, cy = HEAL_NUCLEUS
    rr = np.hypot(x - cx, y - cy)
    band = np.abs(rr - radius) <= thick * 0.6
    if dashes:
        band &= np.cos(np.arctan2(y - cy, x - cx) * dashes) > 0.15
    g.set(band, code)
    return g.a


def big_plus(n, half, arm):
    g = Grid(n)
    x, y = g.cells()
    cx, cy = HEAL_NUCLEUS
    dx, dy = np.abs(x - cx), np.abs(y - cy)
    plus = ((dx <= arm) & (dy <= half)) | ((dy <= arm) & (dx <= half))
    g.set(plus, KICK)
    return g.a


def heal_idle():
    n = HEAL_CELLS
    e = HEAL_ELECTRONS
    er = HEAL_ELECTRON_R
    nr = HEAL_NUCLEUS_R
    nx, ny = HEAL_NUCLEUS
    empty = np.zeros((n, n), dtype=np.uint8)
    frames = [empty.copy()]                                              # 0 rest: the original
    frames.append(lit_ball(n, e[0][0], e[0][1], er, twinkle=2.6))       # 1 top electron lights
    frames.append(streak(n, (e[0][0] + 2.0, e[0][1] + 3.0), (e[1][0] - 2.6, e[1][1] - 3.2)))   # 2 smear
    frames.append(lit_ball(n, e[1][0], e[1][1], er, twinkle=2.6))       # 3 right electron
    frames.append(streak(n, (e[1][0] - 3.2, e[1][1] + 2.4), (e[2][0] + 3.2, e[2][1] + 2.4)))   # 4 smear
    frames.append(lit_ball(n, e[2][0], e[2][1], er, twinkle=2.6))       # 5 left electron
    frames.append(streak(n, (e[2][0] + 2.6, e[2][1] - 2.2), (nx - nr * 0.85, ny + nr * 0.5)))  # 6 smear in
    f7 = lit_ball(n, nx, ny, nr + 0.4, kick=False)                       # 7 nucleus pops
    f7[(f7 > 0) & (np.hypot(*np.mgrid[0:n, 0:n][::-1] + 0.5 - np.array([nx, ny])[:, None, None]) > nr * 0.55)] = BASE
    over(f7, big_plus(n, 2.9, 0.9))
    over(f7, nucleus_ring(n, nr + 1.6, 0, 1.0, LIGHT))
    frames.append(f7)
    f8 = nucleus_ring(n, nr + 3.0, 10, 1.0, LIGHT)                      # 8 shock ring
    over(f8, big_plus(n, 2.3, 0.7))
    frames.append(f8)
    frames.append(nucleus_ring(n, nr + 4.8, 14, 0.8, BASE))             # 9 breaking up
    frames.append(empty.copy())                                          # 10 rest
    return list(zip(frames, HEAL_IDLE_TICKS))


# ----------------------------------------------------------------- output ---
META_TEMPLATE = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
  isReadable: 0
  textureSettings:
    serializedVersion: 2
    filterMode: 0
    aniso: 0
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  spriteMode: 1
  spritePixelsToUnits: {ppu}
  spritePivot: {{x: 0.5, y: 0.5}}
  alphaIsTransparency: 1
  textureType: 8
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    textureCompression: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def write_meta(png, ppu):
    """Pixel-art import: Point filter, no mipmaps, uncompressed, given PPU.
    New files get a guid derived from their path (stable across re-runs);
    existing metas keep their guid and are patched in place."""
    import hashlib
    import re
    meta = png + ".meta"
    ppu_s = ("%.6f" % ppu).rstrip("0").rstrip(".")
    if not os.path.exists(meta):
        rel = os.path.relpath(png, ASSETS).replace(os.sep, "/")
        guid = hashlib.md5(("pause-pixel-atoms/" + rel).encode()).hexdigest()
        with open(meta, "w") as f:
            f.write(META_TEMPLATE.format(guid=guid, ppu=ppu_s))
        return
    text = open(meta).read()
    text = re.sub(r"enableMipMap: \d", "enableMipMap: 0", text)
    text = re.sub(r"filterMode: -?\d", "filterMode: 0", text)
    text = re.sub(r"spritePixelsToUnits: [0-9.]+", "spritePixelsToUnits: " + ppu_s, text)
    text = re.sub(r"textureCompression: \d", "textureCompression: 0", text)
    if "textureCompression" not in text:
        text = text.replace("  spriteSheet:\n", "  platformSettings:\n  - serializedVersion: 4\n"
                            "    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 2048\n"
                            "    textureCompression: 0\n  spriteSheet:\n", 1)
    with open(meta, "w") as f:
        f.write(text)


PPU = {"atom": 700.0, "dust": 180 / 2.56, "dustsm": 44 / 1.28,
       "heal_glint": HEAL_CELLS * HEAL_UP / (944.0 / 180.0)}


def save(img, path, ppu=None):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path, optimize=True)
    if ppu:
        write_meta(path, ppu)


def write_all(preview=False):
    written = {}

    def emit(name, frames, ramp, ink=INK, up=UP):
        imgs = []
        ppu = PPU.get(name.split("_idle")[0], PPU["atom"]) if "idle" in name else \
            PPU["heal_glint"] if name == "heal_glint" else PPU["atom"]
        for i, (cells, ticks) in enumerate(frames):
            img = to_image(cells, ramp, ink, up)
            save(img, os.path.join(OUT_FRAMES, f"{name}_{i}.png"), ppu)
            imgs.append((img, ticks))
        written[name] = imgs
        return imgs

    shield = emit("shield_idle", atom_idle("shield"), RAMPS["shield"])
    pause = emit("pause_idle", atom_idle("pause"), RAMPS["pause"])
    dust = emit("dust_idle", dust_idle(DUST_CELLS), RAMPS["dust"])
    dustsm = emit("dustsm_idle", dust_idle(DUST_SM_CELLS), RAMPS["dust"])
    emit("heal_glint", heal_idle(), RAMPS["heal"], HEAL_INK, HEAL_UP)
    for kind in ("shield", "pause", "dust"):
        emit(f"{kind}_burst", burst(kind), RAMPS[kind])
    emit("heal_burst", burst("heal"), RAMPS["heal"], HEAL_INK)

    # Frame 0 doubles as the prefab's own sprite, so the prefabs keep their
    # sprite guid and a spawned atom looks right before its first Update.
    save(shield[0][0], os.path.join(ASSETS, "Art", "Pickups", "atom3a.png"), PPU["atom"])
    save(pause[0][0], os.path.join(ASSETS, "Art", "Pickups", "pauseAtom.png"), PPU["atom"])
    save(dust[0][0], os.path.join(ASSETS, "Art", "Pickups", "StarDustLarge.png"), PPU["dust"])
    save(dustsm[0][0], os.path.join(ASSETS, "Art", "Pickups", "StarDustSmall.png"), PPU["dustsm"])

    if preview:
        write_previews(written)


# ---------------------------------------------------------------- previews ---
BG = (14, 20, 36, 255)   # Space lane colour #0E1424


def flat(img, bg=BG):
    b = Image.new("RGBA", img.size, bg)
    b.alpha_composite(img)
    return b


def heal_composited(overlay):
    """The original green atom crop with an overlay frame on top."""
    src = Image.open(os.path.join(ASSETS, "Art", "Resources", "Pickups", "heal_atom_green.png")).convert("RGBA")
    x0, y0, side = HEAL_CROP
    crop = src.crop((int(x0), int(y0), int(x0 + side), int(y0 + side)))
    ov = overlay.resize(crop.size, Image.NEAREST)
    crop.alpha_composite(ov)
    return crop


def write_previews(written):
    os.makedirs(PREVIEW, exist_ok=True)
    cell = 160

    def strip(images, name, label_ticks=True):
        w = cell * len(images)
        sheet = Image.new("RGBA", (w, cell), BG)
        for i, (img, _) in enumerate(images):
            im = img.resize((cell - 8, cell - 8), Image.NEAREST)
            sheet.alpha_composite(im, (i * cell + 4, 4))
        sheet.save(os.path.join(PREVIEW, name + "_strip.png"))
        gif = []
        durations = []
        for img, ticks in images:
            gif.append(flat(img.resize((cell * 2, cell * 2), Image.NEAREST)).convert("P", palette=Image.ADAPTIVE))
            durations.append(int(round(ticks * 1000 / 24)))
        gif[0].save(os.path.join(PREVIEW, name + ".gif"), save_all=True, append_images=gif[1:],
                    duration=durations, loop=0, disposal=2)

    heal = [(heal_composited(img), t) for img, t in written["heal_glint"]]
    strip(heal, "heal_idle")
    for name in ("shield_idle", "pause_idle", "dust_idle", "dustsm_idle",
                 "shield_burst", "pause_burst", "dust_burst", "heal_burst"):
        strip(written[name], name)

    # family sheet: frame 0 of each at the same world scale
    fam = Image.new("RGBA", (cell * 4 + 50, cell + 10), BG)
    heal0 = heal_composited(written["heal_glint"][0][0])
    for i, img in enumerate([heal0, written["shield_idle"][0][0], written["pause_idle"][0][0],
                             written["dust_idle"][0][0]]):
        s = cell if i < 3 else int(cell * 0.256 / 0.28)
        im = img.resize((s, s), Image.NEAREST)
        fam.alpha_composite(im, (10 + i * (cell + 10) + (cell - s) // 2, 5 + (cell - s) // 2))
    fam.save(os.path.join(PREVIEW, "atom_family.png"))


if __name__ == "__main__":
    write_all(preview="--preview" in sys.argv)
