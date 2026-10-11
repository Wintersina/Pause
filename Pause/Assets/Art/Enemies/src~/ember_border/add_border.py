#!/usr/bin/env python3
"""Ember enemy readability: a thin pale border round the outer silhouette of the Ember strips (all 11 read WEAK over the Ember tiles in ReadabilitySweep).

Interior art is untouched: the border is written only into pixels that are (almost) transparent and touch the silhouette
(alpha >= SOLID) in the 8-neighbourhood, once per cell, so grids, registration and anchors stay identical. Colour: a pale
pale ember-orange (#FFB870, hue ~27, S .56), opaque; NO pink. Reads from ../ember_recolor/out/enemies (the recoloured, pre-border strips), writes the
live strips in Resources/Enemies. Re-runnable.

    python3 add_border.py [--width 1] [--check]
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
LIVE = HERE.parents[2] / "Resources" / "Enemies"
NAMES = ["ember_alien", "ember_chaser", "ember_big", "ember_fighter_1", "ember_fighter_2", "ember_fighter_3", "ember_fighter_4", "ember_rock_cinder", "ember_rock_islet", "ember_rock_magma", "ember_rock_obsidian"]
BORDER = (255, 222, 166)   # #FFDEA6 pale ember-gold rim (was cream #EBD8BC)
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


def border(strip, width):
    CELL = strip.shape[0]
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
    return break_straight(out, strip, CELL)


def break_straight(out, strip, CELL, limit=12):
    """The border must not create ruler-straight vertical cuts (verify_enemy_strip: < 0.15 x cell): where a column of the
    result has a run of edge texels longer than `limit`, drop every 7th BORDER pixel of that run (the interior is never touched)."""
    for c in range(out.shape[1] // CELL):
        cell = out[:, c * CELL:(c + 1) * CELL]
        orig = strip[:, c * CELL:(c + 1) * CELL]
        for _ in range(4):
            changed = False
            solid = cell[..., 3] > 128
            for side in (-1, 1):
                for x in range(CELL):
                    nx = x + side
                    edge = solid[:, x] & ((cell[:, nx, 3] == 0) if 0 <= nx < CELL else True)
                    run = 0
                    for y in range(CELL):
                        run = run + 1 if edge[y] else 0
                        if run > limit and run % 7 == 0 and orig[y, x, 3] < SOLID:
                            cell[y, x] = (0, 0, 0, 0); solid[y, x] = False; changed = True; run = 0
            if not changed:
                break
    return out


def main():
    width = int(sys.argv[sys.argv.index("--width") + 1]) if "--width" in sys.argv else 1
    for n in NAMES:
        src = np.array(Image.open(HERE.parent / "ember_recolor" / "out" / "enemies" / f"{n}.png").convert("RGBA"))
        res = border(src, 2 if n == "ember_big" else width)
        changed = (res != src).any(axis=2)
        # interior untouched: every pixel that was opaque >= SOLID is identical
        assert (res[src[:, :, 3] >= SOLID] == src[src[:, :, 3] >= SOLID]).all(), n
        ys, xs = np.where(res[:, :, 3] >= 16)
        print(f"{n}: {int(changed.sum())} border px, size {res.shape[1]}x{res.shape[0]}")
        if "--check" not in sys.argv:
            Image.fromarray(res).save(LIVE / f"{n}.png")


if __name__ == "__main__":
    main()
