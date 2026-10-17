#!/usr/bin/env python3
"""Lantern Angler (tide_fighter_3): the blue side wings (plates, struts, jet pods, flames) and their root clamps are removed; everything else is pixel-identical.
  python3 fix_fighter3_wings.py [out.png]   reads flying_src/tide_fighter_3_flying_forward_swept.png (the original master, kept as the record)
                                            default out: flying_src/tide_fighter_3_flying.png"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
SRC = HERE / "flying_src" / "tide_fighter_3_flying_forward_swept.png"
W = 1254
CUT = 272                    # the old wings are everything outside this column
Y0, Y1 = 556, 1060           # vertical extent of a wing incl. pods and flames
CLAMP = (310, 662, 886)      # wing-root clamp left over beside the hull: x < 310, rows 662..886


def main():
    dst = Path(sys.argv[1]) if len(sys.argv) > 1 else HERE / "flying_src" / "tide_fighter_3_flying.png"
    a = np.array(Image.open(SRC).convert("RGBA"))
    cx, c0, c1 = CLAMP
    a[Y0:Y1, :CUT] = 0; a[Y0:Y1, W - CUT:] = 0
    a[c0:c1, :cx] = 0; a[c0:c1, W - cx:] = 0
    a[880:Y1, :cx] = 0; a[880:Y1, W - cx:] = 0
    solid = a[..., 3] > 0                                    # drop stray specks: keep only the large connected pieces
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
        if len(comp) < 400:
            for y, x in comp:
                a[y, x] = 0
    Image.fromarray(a, "RGBA").save(dst)


if __name__ == "__main__":
    main()
