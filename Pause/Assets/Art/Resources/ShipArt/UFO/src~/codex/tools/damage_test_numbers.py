"""Replicates ShipDamageTest's d1 / d2 numbers (non-flash columns, Same = summed |dRGB| <= 24)."""
import sys
import numpy as np
from PIL import Image
for p in sys.argv[1:]:
    a = np.array(Image.open(p).convert("RGBA")).astype(int)
    C = 256
    ch = [0, 0, 0]; op = 0
    for c in range(8):
        r = [a[s * C:(s + 1) * C, c * C:(c + 1) * C] for s in range(3)]
        m = r[0][..., 3] >= 250
        op += m.sum()
        def d(x, y):
            return int((np.abs(x[..., :3] - y[..., :3]).sum(-1) > 24)[m].sum())
        ch[0] += d(r[0], r[1]); ch[1] += d(r[0], r[2]); ch[2] += d(r[1], r[2])
    print(p.split("/")[-1], f"d1 {100*ch[0]/op:.1f}%  d2 {100*ch[1]/op:.1f}%  ratio {ch[1]/ch[0]:.2f}")
