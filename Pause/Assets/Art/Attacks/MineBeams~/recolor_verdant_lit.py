"""Verdant's LIT mine atlas (rail_mines_neon_verdant_lit.png) was painted hot pink: the green mine turned pink when it armed (user, 2026-10-12).
Every pink-family pixel (same mask as recolor_nopink.py / MineCue.IsPink) goes to the Verdant cue green, keeping its lightness structure, with
more saturation than the generic pale remap so the arming core reads as glowing green rather than washed-out mint.
usage: python3 recolor_verdant_lit.py <src pink original> <dest.png>
"""
import sys, colorsys
import numpy as np
from PIL import Image
a = np.array(Image.open(sys.argv[1]).convert("RGBA"))
rgb = a[:, :, :3].astype(np.float32) / 255
mx = rgb.max(2); mn = rgb.min(2); d = mx - mn
s = np.where(mx > 0, d / np.maximum(mx, 1e-4), 0)
r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
h = np.zeros(mx.shape, np.float32)
ix = (mx == r) & (d > 0); h[ix] = ((g - b)[ix] / d[ix]) % 6
ix = (mx == g) & (d > 0) & (mx != r); h[ix] = (b - r)[ix] / d[ix] + 2
ix = (mx == b) & (d > 0) & (mx != r) & (mx != g); h[ix] = (r - g)[ix] / d[ix] + 4
h = (h * 60) % 360
pink = (a[..., 3] > 0) & (d >= .035) & (mx > .25) & (h >= 280) & (h < 352)
out = a.copy()
for y, x in zip(*np.where(pink)):
    sat = min(max(float(s[y, x]) * 1.0, .30), .85)
    v = max(float(mx[y, x]) * .92, .55)
    rr, gg, bb = colorsys.hsv_to_rgb(125 / 360, sat, v)
    out[y, x, :3] = (round(rr * 255), round(gg * 255), round(bb * 255))
Image.fromarray(out, "RGBA").save(sys.argv[2])
print("recoloured px:", int(pink.sum()))
