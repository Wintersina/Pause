#!/usr/bin/env python3
"""Deterministic roundness fix for desert_planet_limb.png (nearest-neighbour column warp, no blending).
Horizon drop (apex 355 -> side edges) is scaled by LAM so the arc is a tighter circle; the rim bands move rigidly
with the arc (plateau P px), the ground below follows with a linear fade to no shift at depth D.
usage: limb_round.py original/desert_planet_limb.png desert_planet_limb.png [LAM]"""
import sys, numpy as np
from PIL import Image
src, dst = sys.argv[1], sys.argv[2]
LAM = float(sys.argv[3]) if len(sys.argv) > 3 else 1.30
P, D, APEX = 130.0, 720.0, 355
a = np.asarray(Image.open(src).convert("RGBA")); H, W = a.shape[:2]
h_old = np.array([np.nonzero(a[:, x, 3] >= 128)[0][0] for x in range(W)], float)
pad=np.pad(h_old,31,mode="edge"); hs=np.median(np.lib.stride_tricks.sliding_window_view(pad,63),axis=1)
hs = np.convolve(np.pad(hs, 32, mode="edge"), np.ones(65) / 65, "valid")
delta = (LAM - 1.0) * (hs - APEX)            # extra drop at each column (0 at apex)
h_new = h_old + delta
out = np.zeros_like(a)
ys = np.arange(H)[:, None].astype(float)
d = ys - h_new[None, :]                       # depth below the new arc
w = np.where(d <= P, 1.0, np.clip(1.0 - (d - P) / (D - P), 0, 1))
ysrc = np.rint(ys - delta[None, :] * w).astype(int)
ysrc = np.clip(ysrc, 0, H - 1)
out = a[ysrc, np.arange(W)[None, :]]
Image.fromarray(out).save(dst)
