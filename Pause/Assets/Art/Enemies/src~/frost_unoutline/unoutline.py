#!/usr/bin/env python3
"""Remove the baked pale outline ring (#DDF3FF, 1-3 px outside the dark ink line) from the Frost elites that still had it.

User rule (Oct 19): no outlines / borders on enemies. The ring is one flat colour sitting OUTSIDE the hull's own dark outline, so it is
peeled deterministically: repeatedly clear every pixel of that colour that touches transparency (4-neighbours), until none is left.
Interior pixels of the same colour (ice highlights inside the dark line) never touch transparency and are kept. Everything else is
byte-identical. Applied to the Codex source strip and its Resources copy (EliteArtSync copies byte for byte).

  python3 unoutline.py <png> [...]           rewrites in place, prints pixels removed per file
"""
import sys
import numpy as np
from PIL import Image

RING = np.array([221, 243, 255])
TOL = 6


def peel(a):
    ring = (np.abs(a[..., :3].astype(int) - RING).sum(2) <= TOL) & (a[..., 3] > 0)
    removed = 0
    while True:
        t = a[..., 3] == 0
        nb = np.zeros_like(t)
        nb[1:] |= t[:-1]; nb[:-1] |= t[1:]; nb[:, 1:] |= t[:, :-1]; nb[:, :-1] |= t[:, 1:]
        hit = ring & nb & (a[..., 3] > 0)
        if not hit.any():
            return removed
        a[hit] = 0
        removed += int(hit.sum())


if __name__ == "__main__":
    for p in sys.argv[1:]:
        a = np.array(Image.open(p).convert("RGBA"))
        n = peel(a)
        Image.fromarray(a, "RGBA").save(p)
        print(p, "removed", n)
