"""terrain reading for the Ember design: 8-texel cells of a 896 x 4096 tile"""
import numpy as np
from PIL import Image
from scipy.ndimage import uniform_filter, gaussian_filter, binary_dilation, label
H = '/private/tmp/claude-501/-Users-sina-Developer/50b8c9d7-da6c-44fe-8c92-736a84a1fed0/scratchpad/pl/ember/r1/'
CELL = 8
def hsv(a):
    mx = a.max(-1); mn = a.min(-1); d = mx - mn + 1e-9; r, g, b = a[..., 0], a[..., 1], a[..., 2]
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    return h, d / np.maximum(mx, 1e-9), mx
def read(tile):
    T = np.asarray(Image.open(tile).convert('RGB')).astype(float) / 255
    h, s, v = hsv(T); R, C = T.shape[0] // CELL, T.shape[1] // CELL
    def cellmean(m): return m[:R * CELL, :C * CELL].reshape(R, CELL, C, CELL).mean((1, 3))
    lava = cellmean(((h >= 4) & (h <= 62) & (s > .5) & (v > .45)).astype(float))
    L = T @ np.array([.3, .59, .11]); rel = cellmean(L ** 2) - cellmean(L) ** 2; rel = np.sqrt(np.maximum(rel, 0))
    # relief over the neighbourhood (a prop's footprint), wraps vertically
    relb = uniform_filter(rel, 5, mode='wrap')
    lavab = uniform_filter(lava, 5, mode='wrap')
    return dict(T=T, lava=lava, rel=rel, relb=relb, lavab=lavab, R=R, C=C, L=L, h=h, s=s, v=v)
