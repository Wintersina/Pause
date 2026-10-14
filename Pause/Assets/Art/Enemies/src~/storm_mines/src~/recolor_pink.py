#!/usr/bin/env python3
"""Storm rod mine: remove the faint mauve / pink cast (NoPinkOutsideSpaceTest: hue 280..352, chroma >= 9/255).

Reads  ../pink_original/rail_mines_neon_storm.png  and writes the Resources atlas. Every pixel the test would
call pink has its hue moved to the Storm steel / storm-white blue (230 deg); value, alpha and the pixel's
Rec.709 luma are kept, resolution untouched, no resampling. Deterministic; re-run any time.

  python3 recolor_pink.py [out.png]
"""
import sys, os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, '..', 'pink_original', 'rail_mines_neon_storm.png')
DST = os.path.join(HERE, '..', '..', '..', '..', 'Resources', 'Enemies', 'Mines', 'rail_mines_neon_storm.png')
TARGET_HUE = 230.0

def hsv(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx = a.max(-1); mn = a.min(-1); d = mx - mn; dd = np.maximum(d, 1e-9)
    h = np.where(mx == r, ((g - b) / dd) % 6, np.where(mx == g, (b - r) / dd + 2, (r - g) / dd + 4)) * 60
    return h, d / np.maximum(mx, 1e-9), mx

def from_hsv(h, s, v):
    c = v * s; hp = (h % 360) / 60.0
    x = c * (1 - np.abs(hp % 2 - 1)); m = v - c
    z = np.zeros_like(h)
    sel = [(hp < 1, (c, x, z)), ((hp >= 1) & (hp < 2), (x, c, z)), ((hp >= 2) & (hp < 3), (z, c, x)),
           ((hp >= 3) & (hp < 4), (z, x, c)), ((hp >= 4) & (hp < 5), (x, z, c)), (hp >= 5, (c, z, x))]
    out = np.zeros(h.shape + (3,))
    for cond, (r, g, b) in sel:
        out[..., 0] = np.where(cond, r, out[..., 0]); out[..., 1] = np.where(cond, g, out[..., 1]); out[..., 2] = np.where(cond, b, out[..., 2])
    return out + m[..., None]

def luma(a): return a @ np.array([.2126, .7152, .0722])

def main():
    dst = sys.argv[1] if len(sys.argv) > 1 else DST
    src = np.array(Image.open(SRC).convert('RGBA'))
    f = src[..., :3].astype(float) / 255
    h, s, v = hsv(f)
    # the test's 8-bit test uses the same HSV; margin: also take the 270..280 and 352..360 fringe so no hue seam is left
    pink = (src[..., 3] > 24) & (v > .25) & (s * v >= .035) & (h >= 270) & (h < 360)
    new = from_hsv(np.full_like(h, TARGET_HUE), s, v)
    ratio = luma(f) / np.maximum(luma(new), 1e-6)
    new = np.clip(new * ratio[..., None], 0, 1)
    out = src.copy()
    out[..., :3][pink] = np.round(new[pink] * 255).astype(np.uint8)
    Image.fromarray(out, 'RGBA').save(dst)
    print('recoloured', int(pink.sum()), 'pixels ->', dst)

main()
