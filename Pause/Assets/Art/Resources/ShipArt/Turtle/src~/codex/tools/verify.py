"""Verification of Turtle's final files (sheet contract, damage progression, holes, skins, flames)."""
import os
import sys
import numpy as np
from PIL import Image

S = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..")
CELL = 256


def load(p):
    im = Image.open(p)
    im.load()
    return np.array(im.convert("RGBA"))


def cell(a, r, c):
    return a[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL]


def enclosed(al):
    h, w = al.shape
    out = np.zeros_like(al)
    st = [(y, x) for y in range(h) for x in range(w) if (y in (0, h - 1) or x in (0, w - 1)) and not al[y, x]]
    for y, x in st:
        out[y, x] = True
    while st:
        y, x = st.pop()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            yy, xx = y + dy, x + dx
            if 0 <= yy < h and 0 <= xx < w and not al[yy, xx] and not out[yy, xx]:
                out[yy, xx] = True
                st.append((yy, xx))
    return int((~al & ~out).sum())


def sheet_report(name, a, stock=None):
    assert a.shape == (768, 2304, 4), a.shape
    al = a[..., 3]
    assert set(np.unique(al)) <= {0, 255}, "alpha not binary"
    base = [cell(a, 0, c)[..., 3] for c in range(9)]
    for r in (1, 2):
        for c in range(9):
            assert (cell(a, r, c)[..., 3] == base[c]).all(), f"{name} row{r} col{c} alpha != row0"
    if stock is not None:
        assert (al == stock[..., 3]).all(), f"{name} alpha != stock"
    holes = sum(enclosed(base[c] > 0) for c in range(9))
    # bbox drift vs idle
    ys, xs = np.nonzero(base[0])
    b0 = (xs.min(), xs.max(), ys.min(), ys.max())
    drift = []
    for c in (1, 2, 3, 4, 5):
        ys, xs = np.nonzero(base[c])
        drift.append((int(xs.min() - b0[0]), int(xs.max() - b0[1]), int(ys.min() - b0[2])))
    widths = []
    for c in (6, 7):
        ys, xs = np.nonzero(base[c])
        widths.append(round((xs.max() - xs.min() + 1) / (b0[1] - b0[0] + 1), 3))
    # damage progression: share of idle-hull pixels that changed row0->row1 and row0->row2 (per cell)
    def frac(r):
        v = []
        for c in range(8):
            d = np.abs(cell(a, r, c)[..., :3].astype(int) - cell(a, 0, c)[..., :3].astype(int)).sum(2) > 30
            v.append(d.sum() / max(1, (base[c] > 0).sum()))
        return np.array(v)
    f1, f2 = frac(1), frac(2)
    print(f"{name}: alpha binary + rows equal OK; enclosed transparent px {holes}; bank widths vs idle {widths}; "
          f"idle-cell bbox drift (dx0,dx1,dy0) {drift}")
    print(f"   changed share row1 {np.round(f1, 3).tolist()}  row2 {np.round(f2, 3).tolist()}  ratio r2/r1 mean "
          f"{(f2.mean() / f1.mean()):.2f}")
    # glints / lights-off differ from idle
    print("   glint/lights cells differ vs idle (%):",
          [round(100 * (np.abs(cell(a, 0, c)[..., :3].astype(int) - cell(a, 0, 0)[..., :3].astype(int)).sum(2) > 60).mean(), 2)
           for c in (2, 3, 4)])


stock = load(os.path.join(S, "Turtle.png"))
sheet_report("stock", stock)
for n in ("Night", "Sodium", "Bone", "Shellback"):
    sheet_report(n, load(os.path.join(S, f"Turtle_{n}.bytes")), stock)
fl = load(os.path.join(S, "Turtle_flame.png"))
print("flame sheet", fl.shape)
