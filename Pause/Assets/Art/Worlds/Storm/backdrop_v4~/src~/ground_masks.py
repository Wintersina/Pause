#!/usr/bin/env python3
"""Affinity masks for the Storm ground tiles (GroundPlacement / GroundMask).

A gas giant has no surface: the whole tile is cloud sea. 'O' (open, calm cloud: platforms, masts and
refineries float here) is the default; 'C' (dense churning thunderhead: only the anvil towers rise from
it) marks the brightest, most textured cloud crests, ranked by percentile per variant. Grid 32 x 64
(32 px cells, row 0 = the TOP of the image), written to <dir>/v<N>/mask.txt.

    python3 ground_masks.py <Resources/Worlds/Storm/Backdrop3 (holds v1..v4)>
"""
import sys, os
import numpy as np
from PIL import Image

CELL = 32          # v4 tiles are 1024x2048: the same 32 x 64 grid as before
CSHARE = {1: .10, 2: .30, 3: .18, 4: .12}

def blur(a, r):
    a = np.concatenate([a[-r:], a, a[:r]], 0)
    c = np.cumsum(np.pad(a, ((1, 0), (0, 0))), 0)
    a = (c[2 * r + 1:] - c[:-2 * r - 1]) / (2 * r + 1)
    a = np.pad(a, ((0, 0), (r, r)), mode='edge')
    c = np.cumsum(np.pad(a, ((0, 0), (1, 0))), 1)
    return (c[:, 2 * r + 1:] - c[:, :-2 * r - 1]) / (2 * r + 1)

def main(root):
    for v in range(1, 3):          # v1, v2 only (v3 keeps its original mask)
        im = np.asarray(Image.open(os.path.join(root, 'v%d' % v, 'mid.png')).convert('RGB')).astype(float) / 255
        lum = im @ np.array([.299, .587, .114])
        gx = np.abs(np.diff(lum, axis=1, append=lum[:, -1:])); gy = np.abs(np.roll(lum, -1, 0) - lum)
        score = blur(lum, 20) + 6 * blur(gx + gy, 20)
        thr = np.quantile(score, 1 - CSHARE[v])
        H, W = lum.shape
        gh, gw = H // CELL, W // CELL
        cell = (score >= thr).reshape(gh, CELL, gw, CELL).mean((1, 3)) > .5
        rows = [''.join('C' if cell[y, x] else 'O' for x in range(gw)) for y in range(gh)]
        open(os.path.join(root, 'v%d' % v, 'mask.txt'), 'w').write('\n'.join(rows) + '\n')
        print('v%d: C share %.2f' % (v, cell.mean()))

if __name__ == '__main__':
    main(sys.argv[1])
