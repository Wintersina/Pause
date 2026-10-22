"""Checks the rendered sheets the same way ShipArtTest does in Unity:
palette compliance (guide palette + the ship's identity hue) and clean alpha
(no faint pixels outside a 2 px band of the ink outline)."""
import os

import hullkit  # noqa: F401  (puts the sample pipeline on sys.path)
import akira
from PIL import Image

from build import CELL, OUT_DIR
from ships import HUES, ORDER

GUIDE = [v for k, v in vars(akira).items() if k.isupper() and isinstance(v, str) and v.startswith("#")]
GUIDE.append("#B9AE98")   # BONE's shadow tone (the sample's stripe-in-shadow)
INKS = [akira.INK, akira.RED]
OPAQUE = 250


def rgb(h):
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))


def near(c, pal, tol):
    return any(sum((a - b) ** 2 for a, b in zip(c, p)) <= tol * tol for p in pal)


def check(key):
    im = Image.open(os.path.join(OUT_DIR, key + ".png")).convert("RGBA")
    W, H = im.size
    px = im.load()
    pal = [rgb(h) for h in GUIDE + list(HUES[key])]
    inks = [rgb(h) for h in INKS]
    opaque = off = faint = stray = 0
    inkmask = set()
    for y in range(H):
        for x in range(W):
            r, g, b, a = px[x, y]
            if a >= OPAQUE and near((r, g, b), inks, 40):
                inkmask.add((x, y))
    for y in range(H):
        for x in range(W):
            r, g, b, a = px[x, y]
            if a >= OPAQUE:
                # a flat fill: the pixel and its 8 neighbours are one colour
                # (antialiased seams between two fills are blends by nature)
                if 0 < x < W - 1 and 0 < y < H - 1 and all(
                        px[x + dx, y + dy][3] >= OPAQUE and
                        sum(abs(p - q) for p, q in zip(px[x + dx, y + dy][:3], (r, g, b))) <= 12
                        for dx in (-1, 0, 1) for dy in (-1, 0, 1)):
                    opaque += 1
                    if not near((r, g, b), pal, 12):
                        off += 1
            elif a > 0:
                faint += 1
                if not any((x + dx, y + dy) in inkmask for dx in range(-2, 3) for dy in range(-2, 3)):
                    stray += 1
    return off / opaque, stray, faint


if __name__ == "__main__":
    for key in ORDER:
        frac, stray, faint = check(key)
        print(f"{key:12s} off-palette {frac * 100:5.2f}%  stray faint px {stray} / {faint}")
