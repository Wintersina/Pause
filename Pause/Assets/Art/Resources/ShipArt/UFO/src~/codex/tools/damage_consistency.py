"""Damage layer (row s vs row 0, per cell) overlap with column 0's layer, after the cell's bob shift. IoU per column."""
import sys
import numpy as np
from PIL import Image
a = np.array(Image.open(sys.argv[1]).convert("RGBA")).astype(int)
C = 256
SHIFT = {0: 0, 1: -4, 2: -4, 3: 0, 4: 0, 5: 4, 6: 0, 7: 0}
def layer(s, c):
    x = a[s * C:(s + 1) * C, c * C:(c + 1) * C]
    y = a[0:C, c * C:(c + 1) * C]
    return (np.abs(x[..., :3] - y[..., :3]).sum(-1) > 60) & (x[..., 3] > 0)
for s in (1, 2):
    base = layer(s, 0)
    out = []
    for c in range(8):
        l = layer(s, c)
        b = np.roll(base, SHIFT[c], axis=0)
        out.append(round(float((l & b).sum() / max(1, (l | b).sum())), 2))
    print("row", s, "IoU vs col 0:", out)
