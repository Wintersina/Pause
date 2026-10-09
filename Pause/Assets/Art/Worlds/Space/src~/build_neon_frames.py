#!/usr/bin/env python3
"""Rewrite neon_frames.json from the art in neon_frames.png.

Codex delivered neon_frames.png as a 1536x1024 sheet (4 planets on the top
half, 4 asteroids on the bottom half, one per ~384 px column) but the json
still described an older 1024 px layout, so every cell cut the art off (a
planet window through its lower limb and the top of the rock below it).

Each art piece is found as the opaque bbox between the empty column valleys,
and its cell is that bbox (planets: padded square on the bbox centre, since
the BackdropPlanet shader's disc is the cell's inscribed circle).
Rects are bottom-origin, as BackdropAtlas expects.

  python3 build_neon_frames.py   (from anywhere; writes next to the png)
"""
import json, os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
DIR = os.path.join(HERE, '..', '..', '..', 'Backgrounds', 'Resources', 'Worlds', 'Space', 'Backdrop')
THR = 40

def pieces(a, y0, y1, count=4, grow=0):
    """Bboxes (x0, y0, x1, y1) of the `count` biggest blobs in rows y0..y1;
    blobs closer than `grow` px (a rock and its floating pebbles) are one."""
    m = a[y0:y1] > THR
    g = m.copy()
    for dy in range(-grow, grow + 1):
        for dx in range(-grow, grow + 1):
            g |= np.roll(np.roll(m, dy, 0), dx, 1)
    lab = np.zeros(g.shape, np.int32)
    n = 0
    sizes = {}
    for sy, sx in zip(*np.nonzero(g)):
        if lab[sy, sx]: continue
        n += 1
        stack = [(sy, sx)]
        lab[sy, sx] = n
        c = 0
        while stack:
            y, x = stack.pop(); c += 1
            for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                if 0 <= ny < g.shape[0] and 0 <= nx < g.shape[1] and g[ny, nx] and not lab[ny, nx]:
                    lab[ny, nx] = n; stack.append((ny, nx))
        sizes[n] = c
    keep = sorted(sorted(sizes, key=sizes.get)[-count:], key=lambda k: np.nonzero((lab == k) & m)[1].min())
    out = []
    for k in keep:
        ys, xs = np.nonzero((lab == k) & m)
        out.append((xs.min(), y0 + ys.min(), xs.max() + 1, y0 + ys.max() + 1))
    return out

def slabs(a, y0, y1, count=4):
    """The asteroids touch their neighbours, so cut them at the emptiest
    column near each quarter of the sheet (their pebbles stay with them)."""
    prof = (a[y0:y1] > THR).sum(0)
    w = a.shape[1]
    cuts = [0] + [k * w // count - 40 + int(np.argmin(prof[k * w // count - 40:k * w // count + 40]))
                  for k in range(1, count)] + [w]
    out = []
    for i in range(count):
        ys, xs = np.nonzero(a[y0:y1, cuts[i]:cuts[i + 1]] > THR)
        out.append((cuts[i] + xs.min(), y0 + ys.min(), cuts[i] + xs.max() + 1, y0 + ys.max() + 1))
    return out

def main():
    png = os.path.join(DIR, 'neon_frames.png')
    a = np.array(Image.open(png).convert('RGBA'))[:, :, 3]
    H, W = a.shape
    out = []
    for name, (y0, y1), square in (('neon_planet', (0, H // 2), True), ('neon_asteroid', (H // 2, H), False)):
        for bx0, by0, bx1, by1 in (pieces(a, y0, y1) if square else slabs(a, y0, y1)):
            i = len([o for o in out if o['n'].startswith(name)])
            w, h = bx1 - bx0, by1 - by0
            if square:
                s = max(w, h)
                cx, cy = (bx0 + bx1) / 2, (by0 + by1) / 2
                cx0, cy0 = int(round(cx - s / 2)), int(round(cy - s / 2))
                w = h = s
                bx0, by0 = cx0, cy0
            out.append({'n': '%s_%02d' % (name, i), 'x': int(bx0), 'y': int(H - by0 - h), 'w': int(w), 'h': int(h)})
    with open(os.path.join(DIR, 'neon_frames.json'), 'w') as f:
        json.dump({'sprites': out}, f, indent=1)
        f.write('\n')
    for s in out: print(s)

if __name__ == '__main__':
    main()
