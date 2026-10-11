#!/usr/bin/env python3
"""Desert enemy readability: a thin pale border round the outer silhouette of the weak-readability Desert strips.

Interior art is untouched: the border is written only into pixels that are (almost) transparent and touch the silhouette
(alpha >= SOLID) in the 8-neighbourhood, once per cell, so grids, registration and anchors stay identical. Colour: a pale
bone (#E6DCC0, inside the Desert palette), opaque; NO pink. Reads from ./original/ (the pre-border strips), writes the
live strips in Resources/Enemies. Re-runnable.

    python3 add_border.py [--width 1] [--check]
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
LIVE = HERE / "strips"
NAMES = ["desert_chaser", "desert_big"]   # the Scarab, then the Sandcrawler (ReadabilitySweep over the real Desert tiles: WEAK on the canyon floor)
CELLS = {"desert_big": 256}
BORDER = (230, 220, 192)   # #E6DCC0 bone
SOLID = 96                 # a pixel this opaque is part of the silhouette
CELL = 192


def dilate(mask):
    out = mask.copy()
    p = np.pad(mask, 1)
    h, w = mask.shape
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dx or dy:
                out |= p[1 + dy:1 + dy + h, 1 + dx:1 + dx + w]
    return out


def border(strip, width, CELL=192):
    a = strip[:, :, 3]
    out = strip.copy()
    h, w = a.shape
    for c in range(w // CELL):
        sl = slice(c * CELL, (c + 1) * CELL)
        solid = a[:, sl] >= SOLID
        grown = solid
        for _ in range(width):
            grown = dilate(grown)
        ring = grown & ~solid & (a[:, sl] < SOLID)
        cell = out[:, sl]
        cell[ring] = (*BORDER, 255)
    return out


def main():
    width = int(sys.argv[sys.argv.index("--width") + 1]) if "--width" in sys.argv else 1
    for n in NAMES:
        src = np.array(Image.open(HERE / "strips" / "original" / f"{n}.png").convert("RGBA"))
        res = border(src, width, CELLS.get(n, CELL))
        changed = (res != src).any(axis=2)
        # interior untouched: every pixel that was opaque >= SOLID is identical
        assert (res[src[:, :, 3] >= SOLID] == src[src[:, :, 3] >= SOLID]).all(), n
        ys, xs = np.where(res[:, :, 3] >= 16)
        print(f"{n}: {int(changed.sum())} border px, size {res.shape[1]}x{res.shape[0]}")
        if "--check" not in sys.argv:
            Image.fromarray(res).save(LIVE / f"{n}.png")


if __name__ == "__main__":
    main()
