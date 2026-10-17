"""Numeric check of built strips: margin, bbox-centre drift, mass-centre drift per cell. python check.py [dir]"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image
H = Path(__file__).parent
sys.path.insert(0, str(H))
from spec import SHIPS
d = H / (sys.argv[1] if len(sys.argv) > 1 else "build")
for n, cell, *_ in SHIPS:
    a = np.array(Image.open(d / f"{n}.png").convert("RGBA"))[..., 3] > 8
    row = []
    for c in range(7):
        m = a[:, c * cell:(c + 1) * cell]
        ys, xs = np.nonzero(m)
        bb = (xs.min(), ys.min(), xs.max(), ys.max())
        marg = min(bb[0], bb[1], cell - 1 - bb[2], cell - 1 - bb[3])
        row.append(((bb[0] + bb[2]) / 2, (bb[1] + bb[3]) / 2, xs.mean(), ys.mean(), marg))
    r = np.array(row)
    print(f"{n:16s} bboxC dx={np.ptp(r[:,0]):5.1f} dy={np.ptp(r[:,1]):5.1f} massC dx={np.ptp(r[:,2]):5.1f} dy={np.ptp(r[:,3]):5.1f} minMargin={int(r[:,4].min())}",
          " ".join(f"{int(v)}" for v in r[:, 4]))
