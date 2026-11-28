"""Ember r1 class grid + context flags from a finished tile (14 texel cells, 64 x 292): W lava (lava share > .30 of the cell),
C rough crust / rock massif (relief in the top 14 % of cells), O the rest; v2 (the plate maze): B = flat grey slabs.
ctx hex digit: 1 Flat (smoothest 45 % of open, no lava within 3 cells), 2 Rocky (open within 4 cells of C, or rougher), 4 Shore (within 6 cells of W),
8 Plain (open)."""
import sys, json
import numpy as np
from PIL import Image
from scipy.ndimage import uniform_filter, binary_dilation
H = '/private/tmp/claude-501/-Users-sina-Developer/50b8c9d7-da6c-44fe-8c92-736a84a1fed0/scratchpad/pl/ember/r1/'
CELL = 14
def hsv(a):
    mx = a.max(-1); mn = a.min(-1); d = mx - mn + 1e-9; r, g, b = a[..., 0], a[..., 1], a[..., 2]
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    return h, d / np.maximum(mx, 1e-9), mx
def make(tag, v):
    T = np.asarray(Image.open(f'{H}tiles/{tag}_mid.png').convert('RGB')).astype(float) / 255
    R, C = T.shape[0] // CELL, T.shape[1] // CELL
    h, s, vv = hsv(T); lava = ((h >= 4) & (h <= 62) & (s > .5) & (vv > .45)).astype(float)
    cl = lava[:R * CELL, :C * CELL].reshape(R, CELL, C, CELL).mean((1, 3))
    L = T @ np.array([.3, .59, .11]); rel = L[:R * CELL, :C * CELL].reshape(R, CELL, C, CELL).std((1, 3)); rel = uniform_filter(rel, 3, mode='wrap')
    Wm = uniform_filter(cl, 3, mode='wrap') > .30
    Cm = (rel > np.quantile(rel, .86)) & ~Wm
    Om = ~Wm & ~Cm
    grey = (s[:R * CELL, :C * CELL].reshape(R, CELL, C, CELL).mean((1, 3)) < .22)
    M = np.full((R, C), 'O')
    M[Cm] = 'C'; M[Wm] = 'W'
    if v == 2: M[Om & grey & (rel < np.quantile(rel, .5))] = 'B'
    openm = np.isin(M, ['O', 'B'])
    near_l = uniform_filter(Wm.astype(float), 7, mode='wrap') > 0
    nl3 = binary_dilation(Wm, iterations=3)
    relo = np.where(openm, rel, 9)
    flat = openm & ~nl3 & (relo <= np.quantile(rel[openm], .45))
    rocky = (openm & binary_dilation(Cm, iterations=4)) | (M == 'C')
    shore = binary_dilation(Wm, iterations=6) & ~Wm & (M != 'C')
    bits = flat * 1 + rocky * 2 + shore * 4 + openm * 8
    open(f'{H}tiles/{tag}_mask.txt', 'w').write('\n'.join(''.join(r) for r in M) + '\n')
    open(f'{H}tiles/{tag}_ctx.txt', 'w').write('\n'.join(''.join('%x' % b for b in r) for r in bits) + '\n')
    rep = dict(W=float(Wm.mean()), C=float(Cm.mean()), O=float(openm.mean()), flat=float(flat.mean()), rocky=float(rocky.mean()), shore=float(shore.mean()))
    print(tag, {k: round(x, 3) for k, x in rep.items()})
if __name__ == '__main__':
    for t in sys.argv[1:]: make(t, int(t[1]))
