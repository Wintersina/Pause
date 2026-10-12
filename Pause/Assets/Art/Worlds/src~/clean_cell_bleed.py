#!/usr/bin/env python3
"""Removes neighbour-cell BLEED from the planet worlds' ground atlases (landmarks, pipes, sites,
fires): thin slivers of an adjacent drawing that the atlas cutter left inside a cell's margin
(e.g. the bottom of a rig's floe skirt showing as a flat sliver floating over Frost's refinery,
the flange of the neighbouring pipe standing beside Desert's straight pipe). In game they draw
as stray fragments hanging in the air next to the piece.

A fragment is removed when it is a separate 8-connected alpha component that is
  - small (< 8% of the drawing's main body, > 30 px),
  - in the outer band of the cell (within 40 px of an edge), and
  - a sliver along that edge: <= 18 px thick across it and at least twice as long along it,
  - not touching the drawing (a pad under a leg within 3 px of the main body is kept).
Particles, droplets, loose floes and rocks (compact blobs, or away from the edges) are kept.

    python3 clean_cell_bleed.py <Resources/Worlds> [--dry] [--worlds Frost,Ember]
Prints every removal (world atlas cell bbox). Originals: version control history.
"""
import json, os, sys
from collections import deque
import numpy as np
from PIL import Image

BAND, THIN = 40, 18

def label(m):
    H, W = m.shape
    lab = np.zeros((H, W), np.int32)
    n = 0
    for y0, x0 in zip(*np.nonzero(m)):
        if lab[y0, x0]: continue
        n += 1
        lab[y0, x0] = n
        q = deque([(y0, x0)])
        while q:
            y, x = q.popleft()
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    yy, xx = y + dy, x + dx
                    if 0 <= yy < H and 0 <= xx < W and m[yy, xx] and not lab[yy, xx]:
                        lab[yy, xx] = n
                        q.append((yy, xx))
    return lab, n

def slivers(alpha, cw, ch):
    m = alpha > 8
    if not m.any(): return []
    lab, n = label(m)
    sizes = np.bincount(lab.ravel(), minlength=n + 1)
    main = sizes[1:].max()
    mainLabel = int(sizes[1:].argmax()) + 1
    out = []
    for k in range(1, n + 1):
        if not (30 < sizes[k] < main * .08): continue
        ys, xs = np.nonzero(lab == k)
        x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
        w, h = x1 - x0 + 1, y1 - y0 + 1
        edge_lr = x0 <= BAND or x1 >= cw - 1 - BAND
        edge_tb = y0 <= BAND or y1 >= ch - 1 - BAND
        lr = edge_lr and (x0 <= BAND and x1 <= BAND + THIN or x1 >= cw - 1 - BAND and x0 >= cw - 1 - BAND - THIN) and w <= THIN and h >= 2 * w
        tb = edge_tb and (y0 <= BAND and y1 <= BAND + THIN or y1 >= ch - 1 - BAND and y0 >= ch - 1 - BAND - THIN) and h <= THIN and w >= 2 * h
        if not (lr or tb): continue
        # a pad or shadow just under / beside the drawing (within 3 px of its main body) is part of it
        body = lab == mainLabel
        near = body[max(0, y0 - 3):y1 + 4, max(0, x0 - 3):x1 + 4]
        if near.any(): continue
        out.append((k, (int(x0), int(y0), int(x1), int(y1)), lab))
    return out

def main():
    base = sys.argv[1]
    dry = '--dry' in sys.argv
    worlds = sys.argv[sys.argv.index('--worlds') + 1].split(',') if '--worlds' in sys.argv else ['Frost', 'Verdant', 'Ember', 'Tide', 'Desert']
    for w in worlds:
        for a in ('landmarks', 'pipes', 'sites', 'fires'):
            p = os.path.join(base, w, 'Backdrop3', a + '.png')
            if not os.path.exists(p): continue
            im = np.asarray(Image.open(p).convert('RGBA')).copy()
            H = im.shape[0]
            changed = 0
            for s in json.load(open(p[:-4] + '.json'))['sprites']:
                x, y, cw, ch = s['x'], s['y'], s['w'], s['h']
                top = H - y - ch
                cell = im[top:top + ch, x:x + cw]
                for k, box, lab in slivers(cell[..., 3], cw, ch):
                    print('%s %s %s bleed %s' % (w, a, s['n'], box))
                    if not dry:
                        cell[lab == k] = 0
                        changed += 1
            if changed:
                Image.fromarray(im, 'RGBA').save(p)

if __name__ == '__main__':
    main()
