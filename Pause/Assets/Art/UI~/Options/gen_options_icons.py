#!/usr/bin/env python3
"""Draws the Options volume art pixel by pixel (no scaling filters).

  python3 gen_options_icons.py        -> ../../Resources/Options/*.png

Outputs (all drawn on a 24-px logical grid / 2 and shown 1:1 in canvas units):
  options_icons.png   192x96  4 cells (48 px) per row: muted | 1 | 2 | 3 waves / bars
                      row 0 (top) = speaker with sound waves (effects)
                      row 1       = beamed music note with equaliser bars (music)
  options_track.png   32x16   9-slice track (border 8 left/right)
  options_fill.png    32x16   9-slice neon fill (border 8 left/right)
  options_handle.png  24x36   chunky slider handle
Palette = AkiraPalette (cyan family; red only for the mute slash, never pink).
"""
import math, os
from PIL import Image

INK = (0x14, 0x0C, 0x14, 255)
CYAN = (0x6E, 0xF2, 0xEE, 255)
TEAL = (0x1F, 0xB5, 0xB9, 255)
TEAL_SH = (0x0F, 0x5E, 0x6A, 255)
HI = (0xE6, 0xFF, 0xFC, 255)
NIGHT = (0x0E, 0x14, 0x24, 255)
HAIR = (0x2E, 0x35, 0x60, 255)
MUTED = (0x8C, 0x93, 0xB8, 255)
MUTED_SH = (0x4C, 0x52, 0x78, 255)
RED = (0xD8, 0x23, 0x2C, 255)
RED_HI = (0xFF, 0x5B, 0x45, 255)
N = 24  # logical grid
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Resources", "Options")


def grid(): return [[None] * N for _ in range(N)]


def inside_poly(x, y, pts):
    c = False
    j = len(pts) - 1
    for i in range(len(pts)):
        xi, yi = pts[i]; xj, yj = pts[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi: c = not c
        j = i
    return c


def fill(g, pred, tag):
    for y in range(N):
        for x in range(N):
            if pred(x + .5, y + .5): g[y][x] = tag


def shade(g, body_pal, ink=True):
    """tag grid -> colour grid: ink outline, hi top edge, shadow bottom edge."""
    body, hi, sh = body_pal
    out = [[None] * N for _ in range(N)]
    def has(x, y): return 0 <= x < N and 0 <= y < N and g[y][x] is not None
    for y in range(N):
        for x in range(N):
            if has(x, y):
                out[y][x] = hi if not has(x, y - 1) else (sh if not has(x, y + 1) else body)
    if ink:
        for y in range(-1, N + 1):
            for x in range(-1, N + 1):
                if not has(x, y) and any(has(x + dx, y + dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                    if 0 <= x < N and 0 <= y < N: out[y][x] = INK
    return out


def arc(cx, cy, r, th=1.7, half=52):
    def p(x, y):
        d = math.hypot(x - cx, y - cy)
        a = math.degrees(math.atan2(y - cy, x - cx))
        return abs(d - r) <= th / 2 and abs(a) <= half
    return p


def speaker(level):
    g = grid()
    fill(g, lambda x, y: 1 <= x <= 5 and 8.5 <= y <= 14.5, 1)                        # magnet box
    fill(g, lambda x, y: inside_poly(x, y, [(5, 8.5), (11.5, 3.5), (11.5, 19.5), (5, 14.5)]), 1)  # cone
    for i in range(level):
        fill(g, arc(11.5, 11.5, 4.2 + i * 3.1, 1.7, 48), 2)
    return g


def note(level):
    g = grid()
    def head(cx, cy):
        return lambda x, y: ((x - cx) / 3.1) ** 2 + ((y - cy) / 2.5) ** 2 <= 1
    for cx, cy, sx in ((4.5, 18.5, 6.0), (11.5, 16.5, 13.0)):
        fill(g, head(cx, cy), 1)
        fill(g, lambda x, y, sx=sx, cy=cy: sx <= x <= sx + 1.8 and 4.5 <= y <= cy, 1)
    fill(g, lambda x, y: inside_poly(x, y, [(6, 4.5), (14.2, 2.8), (14.2, 6.2), (6, 8.0)]), 1)  # beam
    for i in range(level):
        h = 5 + i * 3
        x0 = 16.0 + i * 2.5
        fill(g, lambda x, y, x0=x0, h=h: x0 <= x <= x0 + 1.6 and 21 - h <= y <= 21, 2)
    return g


def paint(g, pal, ring_pal=None):
    """Body tag 1 and wave tag 2 shaded separately, then merged (ink wins nowhere over body)."""
    a = [[1 if t == 1 else None for t in r] for r in g]
    b = [[1 if t == 2 else None for t in r] for r in g]
    ca = shade(a, pal)
    cb = shade(b, ring_pal or pal)
    out = [[None] * N for _ in range(N)]
    for y in range(N):
        for x in range(N):
            if ca[y][x] and ca[y][x] != INK: out[y][x] = ca[y][x]
            elif cb[y][x] and cb[y][x] != INK: out[y][x] = cb[y][x]
            elif ca[y][x] or cb[y][x]: out[y][x] = INK
    return out


def slash(c):
    # thick diagonal bar top-left to bottom-right with ink edge
    def on(x, y, w): return abs((y - 3.0) - (x - 3.0) * (17.0 / 17.0)) <= w and 3 <= x <= 21 and 3 <= y <= 21
    for y in range(N):
        for x in range(N):
            if on(x + .5, y + .5, 2.4) and not on(x + .5, y + .5, 1.2): c[y][x] = INK
    for y in range(N):
        for x in range(N):
            if on(x + .5, y + .5, 1.2): c[y][x] = RED_HI if on(x + .5, y + .5, .5) else RED


def cell(kind, level):
    g = (speaker if kind == 0 else note)(level)
    if level == 0:
        c = paint(g, (MUTED, (0xC6, 0xCB, 0xE6, 255), MUTED_SH))
        slash(c)
    else:
        c = paint(g, (CYAN, HI, TEAL), (CYAN, HI, TEAL))
    return c


def blit(img, c, ox, oy, k=2):
    for y in range(N):
        for x in range(N):
            if c[y][x]:
                for dy in range(k):
                    for dx in range(k): img.putpixel((ox + x * k + dx, oy + y * k + dy), c[y][x])


def icons():
    img = Image.new("RGBA", (192, 96), (0, 0, 0, 0))
    for kind in (0, 1):
        for level in range(4): blit(img, cell(kind, level), level * 48, kind * 48)
    img.save(os.path.join(OUT, "options_icons.png"))


def bar(w, h, rows, ends, name):
    """Logical w x h bar (2x scaled). rows: colour per logical row; ends: inset per row at both ends."""
    img = Image.new("RGBA", (w * 2, h * 2), (0, 0, 0, 0))
    for y in range(h):
        for x in range(w):
            ins = ends[y]
            if ins <= x < w - ins:
                col = rows[y]
                for dy in range(2):
                    for dx in range(2): img.putpixel((x * 2 + dx, y * 2 + dy), col)
    img.save(os.path.join(OUT, name))


def handle():
    w, h = 12, 18
    img = Image.new("RGBA", (w * 2, h * 2), (0, 0, 0, 0))
    def put(x, y, col):
        for dy in range(2):
            for dx in range(2): img.putpixel((x * 2 + dx, y * 2 + dy), col)
    for y in range(h):
        for x in range(w):
            corner = (x in (0, w - 1)) and (y in (0, h - 1))
            if corner: continue
            edge = x in (0, w - 1) or y in (0, h - 1)
            if edge: put(x, y, INK); continue
            if y == 1: col = HI
            elif y >= h - 3: col = TEAL
            elif x == 1: col = HI
            elif x == w - 2: col = CYAN
            else: col = CYAN
            put(x, y, col)
    for y in (6, 9, 12):   # grip ridges
        for x in range(3, w - 3): put(x, y, TEAL_SH)
    img.save(os.path.join(OUT, "options_handle.png"))


def main():
    os.makedirs(OUT, exist_ok=True)
    icons()
    # track: 16 x 8 logical. ink rim, night body, hairline highlight inside the top rim.
    bar(16, 8, [INK, HAIR, NIGHT, NIGHT, NIGHT, NIGHT, TEAL_SH, INK], [3, 2, 1, 0, 0, 1, 2, 3], "options_track.png")
    # fill: same silhouette inset, neon cyan with a pale top row
    bar(16, 8, [(0, 0, 0, 0), HI, CYAN, CYAN, CYAN, TEAL, TEAL_SH, (0, 0, 0, 0)], [8, 3, 2, 1, 1, 2, 3, 8], "options_fill.png")
    handle()


if __name__ == "__main__":
    main()
