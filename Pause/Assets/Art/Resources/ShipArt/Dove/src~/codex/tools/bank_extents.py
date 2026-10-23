#!/usr/bin/env python3
"""Bank pose check on stock + every skin: left/right extents of each bank cell about the idle canopy axis, relative to
idle's; bank left needs left < right, bank right needs right < left by >= 0.05; width change vs idle < 22%; skin bank
alpha == stock bank alpha."""
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
DOVE = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
names = ["Dove.png"] + [f"Dove_{n}.bytes" for n in ("Night", "Sodium", "Bone", "Courier")]
stock = np.array(Image.open(os.path.join(DOVE, "Dove.png")).convert("RGBA"))
ok = True
for f in names:
    a = np.array(Image.open(os.path.join(DOVE, f)).convert("RGBA"))
    for r in range(3):
        cell = lambda c: a[r * 256:(r + 1) * 256, c * 256:(c + 1) * 256, 3] > 0
        xs0 = np.nonzero(cell(0).any(0))[0]
        axis = 127.5
        L0, R0 = axis - xs0.min(), xs0.max() - axis
        res = []
        for c, name in ((6, "bankL"), (7, "bankR")):
            xs = np.nonzero(cell(c).any(0))[0]
            ax = axis + 6 * (-1 if c == 6 else 1)      # the canopy axis moves with the 6 px shift
            l, rr = (ax - xs.min()) / L0, (xs.max() - ax) / R0
            wchg = (xs.max() - xs.min() + 1) / (xs0.max() - xs0.min() + 1) - 1
            good = (l < rr - .05) if c == 6 else (rr < l - .05)
            good &= abs(wchg) < .22
            good &= (a[r * 256:(r + 1) * 256, c * 256:(c + 1) * 256, 3] == stock[r * 256:(r + 1) * 256, c * 256:(c + 1) * 256, 3]).all()
            ok &= bool(good)
            res.append(f"{name} left {l:.2f} right {rr:.2f} width {wchg:+.1%} {'ok' if good else 'FAIL'}")
        print(f"{f:18s} row {r}: " + " | ".join(res))
print("ALL OK" if ok else "FAILURES")
