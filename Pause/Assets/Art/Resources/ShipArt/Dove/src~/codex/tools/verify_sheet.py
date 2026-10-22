#!/usr/bin/env python3
"""usage: verify_sheet.py <sheet.png|bytes> [stock sheet for alpha compare]. Contract + damage + hole + flash checks."""
import sys
import numpy as np
from PIL import Image
from collections import deque

im = np.array(Image.open(sys.argv[1]).convert("RGBA"))
stock = np.array(Image.open(sys.argv[2]).convert("RGBA")) if len(sys.argv) > 2 else im
assert im.shape == (768, 2304, 4), im.shape
cell = lambda a, r, c: a[r * 256:(r + 1) * 256, c * 256:(c + 1) * 256]
print("alpha binary:", set(np.unique(im[..., 3])) <= {0, 255}, " alpha == stock:", (im[..., 3] == stock[..., 3]).all())
print("rows share row0 alpha:", all((cell(im, r, c)[..., 3] == cell(im, 0, c)[..., 3]).all() for r in (1, 2) for c in range(9)))

def enclosed(a):
    h, w = a.shape
    seen = np.zeros_like(a, bool)
    q = deque((y, x) for y in range(h) for x in range(w) if (y in (0, h - 1) or x in (0, w - 1)) and not a[y, x])
    for y, x in q:
        seen[y, x] = True
    while q:
        y, x = q.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            yy, xx = y + dy, x + dx
            if 0 <= yy < h and 0 <= xx < w and not a[yy, xx] and not seen[yy, xx]:
                seen[yy, xx] = True
                q.append((yy, xx))
    return int((~a & ~seen).sum())

print("enclosed holes:", sum(enclosed(cell(im, r, c)[..., 3] > 0) for r in range(3) for c in range(9)))
w0 = None
for c in range(9):
    a = cell(im, 0, c)[..., 3] > 0
    xs = np.nonzero(a.any(0))[0]; ys = np.nonzero(a.any(1))[0]
    wd = xs.max() - xs.min() + 1
    if c == 0:
        w0 = (wd, a.sum())
    print(f"col {c}: bbox x{xs.min()}-{xs.max()} y{ys.min()}-{ys.max()} width {wd} ({wd / w0[0] - 1:+.1%}) area {a.sum() / w0[1] - 1:+.1%}")
def diff(a, b):
    m = a[..., 3] > 0
    return float((np.abs(a[..., :3].astype(int) - b[..., :3].astype(int)).sum(-1) > 40)[m].mean() * 100)
for c in range(8):
    print(f"col {c}: row0->1 {diff(cell(im,0,c), cell(im,1,c)):.1f}%  row1->2 {diff(cell(im,1,c), cell(im,2,c)):.1f}%  row0->2 {diff(cell(im,0,c), cell(im,2,c)):.1f}%")
f = cell(im, 0, 8)
print("flash solid:", (f[..., 3] == cell(im, 0, 0)[..., 3]).all() or "bank-style alpha differs from idle (expected: hull of col 0 only)")
