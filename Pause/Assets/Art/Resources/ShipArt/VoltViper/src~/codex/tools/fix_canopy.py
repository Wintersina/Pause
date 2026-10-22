"""Codex left chroma-green streaks inside the canopy glass of key_state0 (they would key out as holes).
Repaint those pixels as canopy cyan, using the nearest non-green canopy colour in the same row."""
import sys
import numpy as np
from PIL import Image

p = sys.argv[1]
a = np.array(Image.open(p).convert("RGB")).astype(int)
r, g, b = a[..., 0], a[..., 1], a[..., 2]
gm = (g > 100) & (g > r * 1.55) & (g > b * 1.4) & (g - r > 65) & (g - b > 65)
# only inside the canopy window (background green is untouched)
box = np.zeros_like(gm)
box[200:520, 380:660] = True
fix = gm & box
out = a.copy()
H, W = fix.shape
for y, x in zip(*np.nonzero(fix)):
    best = None
    for d in range(1, 40):
        for xx in (x + d, x - d):
            if 0 <= xx < W and not gm[y, xx]:
                best = a[y, xx]
                break
        if best is not None:
            break
    out[y, x] = best if best is not None else (60, 200, 215)
print("repainted", int(fix.sum()))
Image.fromarray(out.astype(np.uint8)).save(p)
