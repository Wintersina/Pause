#!/usr/bin/env python3
"""Exact-circle limb: horizon becomes a true circular arc of radius R through the apex (1024, 355).
Each column is shifted DOWN rigidly by (new horizon - old horizon) with nearest-neighbour row picking: detail keeps
its 1:1 aspect (no compression); the rows pushed past the bottom are cropped. Rim bands + 1 px outline move with the arc.
usage: limb_round_exact.py original/desert_planet_limb.png out.png R"""
import sys, numpy as np
from PIL import Image
src, dst, R = sys.argv[1], sys.argv[2], float(sys.argv[3])
a = np.asarray(Image.open(src).convert("RGBA")); H, W = a.shape[:2]
h_old = np.array([np.nonzero(a[:, x, 3] >= 128)[0][0] for x in range(W)], float)
x = np.arange(W) + 0.5 - W / 2           # column centre relative to the image centre
h_new = 355 + R - np.sqrt(R * R - x * x)
delta = np.rint(h_new - h_old).astype(int)
ys = np.arange(H)[:, None]
ysrc = ys - delta[None, :]
valid = (ysrc >= 0) & (ysrc < H)
out = a[np.clip(ysrc, 0, H - 1), np.arange(W)[None, :]].copy()
out[~valid] = 0
Image.fromarray(out).save(dst)
print("R", R, "edge (virtual) y at x=0:", round(h_new[0], 1), " x=2047:", round(h_new[-1], 1))
