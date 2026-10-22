"""Numeric checks for the Ligher sheets (skill step 6)."""
import os
import numpy as np
from PIL import Image
D = os.path.dirname(os.path.abspath(__file__))
S = os.path.join(D, '..')
CELL = 256
def load(n): return np.array(Image.open(os.path.join(S, n)).convert('RGBA'))
stock = load('Ligher.png')
print('size', stock.shape[1], stock.shape[0])
a = stock[..., 3]
print('alpha binary:', set(np.unique(a)) <= {0, 255})
a0 = a[:CELL]
print('rows1-2 alpha == row0:', all((a[r * CELL:(r + 1) * CELL] == a0).all() for r in (1, 2)))
def diff(r, c, r2=0):
    x = stock[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL]
    y = stock[r2 * CELL:(r2 + 1) * CELL, c * CELL:(c + 1) * CELL]
    m = x[..., 3] > 0
    return (np.abs(x[..., :3].astype(int) - y[..., :3].astype(int)).sum(2) > 40)[m].mean() * 100
for c in range(8):
    print('col', c, 'row0->1 %.1f%%  row1->2 %.1f%%' % (diff(1, c), diff(2, c, 1)))
# damage ratio: mean abs diff of row r vs row 0 per pixel (idle)
def mad(r): return np.abs(stock[r * CELL:(r + 1) * CELL, :256, :3].astype(int) - stock[:CELL, :256, :3].astype(int)).sum(2)[a0[:, :256] > 0].mean()
print('MAD 1 %.2f 2 %.2f ratio %.2f' % (mad(1), mad(2), mad(2) / mad(1)))
# bbox drift
def bb(c):
    ys, xs = np.nonzero(a0[:, c * CELL:(c + 1) * CELL]); return (xs.min() + xs.max()) / 2, (ys.min() + ys.max()) / 2, xs.max() - xs.min() + 1, ys.max() - ys.min() + 1
b0 = bb(0)
for c in range(9):
    b = bb(c); print('cell', c, 'cx %.1f cy %.1f w %d h %d' % b, 'dw %+.0f%%' % ((b[2] / b0[2] - 1) * 100))
# enclosed transparent
def enclosed(m):
    h, w = m.shape; seen = np.zeros_like(m); st = [(0, 0)]
    out = ~m
    from collections import deque
    q = deque([(y, x) for y in (0, h - 1) for x in range(w) if out[y, x]] + [(y, x) for x in (0, w - 1) for y in range(h) if out[y, x]])
    for y, x in q: seen[y, x] = True
    while q:
        y, x = q.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            yy, xx = y + dy, x + dx
            if 0 <= yy < h and 0 <= xx < w and out[yy, xx] and not seen[yy, xx]:
                seen[yy, xx] = True; q.append((yy, xx))
    return int((out & ~seen).sum())
print('enclosed transparent px (row0 cell0):', enclosed(a0[:, :256] > 0))
for n in ('Night', 'Amber', 'Bone', 'Flint'):
    s = load('Ligher_%s.bytes' % n)
    print(n, 'size', s.shape[:2], 'alpha==stock', (s[..., 3] == a).all(), 'pink-ish px', int(((s[..., 0] > 200) & (s[..., 2] > 150) & (s[..., 1] < 100) & (a > 0)).sum()))
# green leak
g = (stock[..., 1] > 150) & (stock[..., 0] < 80) & (stock[..., 2] < 80) & (a > 0)
print('green px in stock', int(g.sum()))
