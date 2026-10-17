#!/usr/bin/env python3
"""Lantern Angler: take the painted tell A / tell B / hit cells (4-6, PR #54, src~/tide_storm_poses) and erase the side wings from them.
Cells 0-3 come from the wingless idle strip (fix_fighter3_wings.py + build.py). Wings are everything beyond a cut column on each flank, in the wing band
(rows 80-172, below the lantern); specks are dropped afterwards.
  python3 fix_fighter3_poses.py <poses_strip_with_wings.png> <wingless_idle_strip.png> <out.png>"""
import sys
import numpy as np
from PIL import Image

CELL = 192
CUTS = {4: (52, 138), 5: (56, 136), 6: (56, 134)}      # (left, right) hull flank columns inside each cell
BAND = (80, 172)


def specks(a, min_px=40):
    solid = a[..., 3] > 0
    seen = np.zeros_like(solid)
    for sy, sx in zip(*np.nonzero(solid)):
        if seen[sy, sx]:
            continue
        comp, stack = [], [(sy, sx)]
        seen[sy, sx] = True
        while stack:
            y, x = stack.pop(); comp.append((y, x))
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    ny, nx = y + dy, x + dx
                    if 0 <= ny < a.shape[0] and 0 <= nx < a.shape[1] and solid[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True; stack.append((ny, nx))
        if len(comp) < min_px:
            for y, x in comp:
                a[y, x] = 0


def main():
    poses = np.array(Image.open(sys.argv[1]).convert("RGBA"))
    out = np.array(Image.open(sys.argv[2]).convert("RGBA"))
    for c, (l, r) in CUTS.items():
        cell = poses[:, c * CELL:(c + 1) * CELL].copy()
        yc, hh = (BAND[0] + BAND[1]) / 2, (BAND[1] - BAND[0]) / 2
        for y in range(BAND[0], BAND[1]):                 # the cut follows the hull flank (a shallow arc), never a long straight edge
            bulge = int(round(7 * ((y - yc) / hh) ** 2))
            cell[y, :l + bulge] = 0
            cell[y, r - bulge:] = 0
        for _ in range(5):                                # trim one-pixel slivers left at the cut columns
            al = cell[..., 3] > 0
            n = sum(np.roll(np.roll(al, dy, 0), dx, 1) for dy in (-1, 0, 1) for dx in (-1, 0, 1) if (dy, dx) != (0, 0))
            zone = np.zeros_like(al); zone[:, max(0, l - 8):l + 8] = True; zone[:, r - 8:r + 8] = True
            cell[al & zone & (n <= 4)] = 0
        specks(cell)
        out[:, c * CELL:(c + 1) * CELL] = cell
    Image.fromarray(out, "RGBA").save(sys.argv[3])


if __name__ == "__main__":
    main()
