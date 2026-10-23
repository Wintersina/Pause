#!/usr/bin/env python3
"""Lightning: keep only a few BOLD damage features in the idle damaged cells (column 0, rows 1-2).

Codex's damaged keys re-render the whole hull, which shows up as speckle once the overlay maps the damage
onto the other cells. Here row 1 / row 2 of column 0 = the intact cell everywhere EXCEPT inside hand placed
damage zones (ellipses, cell px). Row 2 keeps row 1's features (state-1 cell inside S1 zones) and adds
its own bigger ones (state-2 cell inside S2 zones). Alpha is never touched. Run before damage_overlay.py:

    python3 compose_damage.py            (stock Lightning.png + the four skins, in place)
    python3 ../../../../src~/damage_overlay.py Lightning --skins
"""
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SHIP = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
CELL = 256
# (cx, cy, rx, ry) in 256 px cell coordinates of the idle pose
S1 = [(78, 112, 18, 29), (54, 156, 28, 24), (200, 156, 20, 18), (84, 204, 15, 24), (122, 162, 12, 17)]
S2 = [(128, 84, 25, 35), (79, 95, 21, 54), (134, 158, 28, 45), (229, 187, 25, 33), (178, 208, 20, 31),
      (168, 150, 16, 23)]
FILES = ["Lightning.png", "Lightning_Night.bytes", "Lightning_Sodium.bytes", "Lightning_Bone.bytes"]


def ell(zones):
    yy, xx = np.mgrid[0:CELL, 0:CELL].astype(float)
    m = np.zeros((CELL, CELL), bool)
    for cx, cy, rx, ry in zones:
        m |= ((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2 <= 1.0
    return m


THR = 60   # summed RGB difference that counts as damage


def plating(a):
    """plain yellow armour (hue 38-68 deg): re-shaded plating, never damage"""
    r, g, b = [a[..., i].astype(float) / 255 for i in range(3)]
    mx, mn = np.maximum(np.maximum(r, g), b), np.minimum(np.minimum(r, g), b)
    d = np.maximum(mx - mn, 1e-6)
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    return (h >= 38) & (h <= 68) & (s > 0.45) & (mx > 0.3)


def strong(a, c0):
    d = np.abs(a[..., :3].astype(int) - c0[..., :3].astype(int)).sum(-1)
    return (d > THR) & (c0[..., 3] > 0) & ~(plating(a) & plating(c0))


def grow(m, c0):
    g = m.copy()
    g[1:] |= m[:-1]
    g[:-1] |= m[1:]
    g[:, 1:] |= m[:, :-1]
    g[:, :-1] |= m[:, 1:]
    return g & (c0[..., 3] > 0)


def main():
    m1, m2 = ell(S1), ell(S2)
    for f in FILES:
        p = os.path.join(SHIP, f)
        a = np.array(Image.open(p).convert("RGBA"))
        c0 = a[0:CELL, 0:CELL].copy()
        s1 = a[CELL:2 * CELL, 0:CELL].copy()
        s2 = a[2 * CELL:3 * CELL, 0:CELL].copy()
        # only pixels that really are damage (strong change vs the intact paint, grown by 1 px) are taken; plating
        # that Codex merely re-shaded stays the intact paint, so nothing is misaligned when the overlay warps it
        d_s1 = grow(strong(s1, c0) & m1, c0)
        d_s2 = grow(strong(s2, c0) & m2, c0)
        r1 = np.where(d_s1[..., None], s1, c0)
        r2 = np.where(d_s1[..., None], s1, c0)
        r2 = np.where(d_s2[..., None], s2, r2)
        r1[..., 3] = c0[..., 3]
        r2[..., 3] = c0[..., 3]
        a[CELL:2 * CELL, 0:CELL] = r1
        a[2 * CELL:3 * CELL, 0:CELL] = r2
        Image.fromarray(a).save(p, format="PNG")
        al = c0[..., 3] > 0
        d1 = (np.abs(r1[..., :3].astype(int) - c0[..., :3].astype(int)).sum(-1) > 24) & al
        d2 = (np.abs(r2[..., :3].astype(int) - c0[..., :3].astype(int)).sum(-1) > 24) & al
        print("%-24s row1 %.1f%%  row2 %.1f%% of the hull" % (f, 100 * d1.sum() / al.sum(), 100 * d2.sum() / al.sum()))


if __name__ == "__main__":
    main()
