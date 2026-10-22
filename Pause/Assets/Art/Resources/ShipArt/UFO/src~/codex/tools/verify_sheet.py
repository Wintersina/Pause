"""python3 verify_sheet.py <sheet.png|bytes> <dark_out.png>  -- contract numbers for a UFO hull or skin sheet."""
import sys, os
import numpy as np
from PIL import Image

def enclosed(al):
    out = np.zeros_like(al)
    out[0], out[-1], out[:, 0], out[:, -1] = ~al[0], ~al[-1], ~al[:, 0], ~al[:, -1]
    while True:
        n = out.copy()
        n[1:] |= out[:-1]; n[:-1] |= out[1:]; n[:, 1:] |= out[:, :-1]; n[:, :-1] |= out[:, 1:]
        n &= ~al
        if (n == out).all():
            break
        out = n
    return int((~al & ~out).sum())

stock = np.array(Image.open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "UFO.png")).convert("RGBA")).astype(int)
im = Image.open(sys.argv[1]).convert("RGBA")
a = np.array(im).astype(int)
print("size", im.size, "alpha values", sorted(set(np.unique(a[..., 3]).tolist())))
print("alpha == stock alpha:", bool((a[..., 3] == stock[..., 3]).all()))
C = 256
def cell(s, c):
    return a[s * C:(s + 1) * C, c * C:(c + 1) * C]
holes = 0
for s in range(3):
    for c in range(9):
        al = cell(s, c)[..., 3] > 0
        holes += enclosed(al)
print("enclosed transparent px (all 27 cells):", holes)
print("rows 1-2 alpha == row 0:", all((cell(s, c)[..., 3] == cell(0, c)[..., 3]).all() for s in (1, 2) for c in range(9)))
def diff(x, y):
    m = (x[..., 3] > 0)
    return 100 * (np.abs(x[..., :3] - y[..., :3]).sum(-1) > 60)[m].mean()
for c in range(8):
    print(f"col {c}: row1 vs row0 {diff(cell(1, c), cell(0, c)):5.1f}%  row2 vs row1 {diff(cell(2, c), cell(1, c)):5.1f}%  row0 vs idle {diff(cell(0, c), cell(0, 0)):5.1f}%")
for s in range(3):
    ref = cell(s, 0)[..., 3] > 0
    ys, xs = np.nonzero(ref)
    out = []
    for c in range(9):
        al = cell(s, c)[..., 3] > 0
        y, x = np.nonzero(al)
        out.append((x.min(), y.min(), x.max() - x.min() + 1, y.max() - y.min() + 1))
    print("row", s, "bbox x,y,w,h per col:", out)
bg = Image.new("RGBA", im.size, (14, 20, 36, 255))
bg.alpha_composite(im)
bg.save(sys.argv[2])
