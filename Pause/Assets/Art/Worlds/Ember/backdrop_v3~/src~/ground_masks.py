#!/usr/bin/env python3
"""Affinity masks for the Ember ground tiles (GroundPlacement / GroundMask).

For each variant's mid.png (the tile the landmarks are pinned to) this
classifies the ground into a coarse grid of 16 px cells (32 x 64 for the
512 x 1024 tile, row 0 = the TOP of the image) and writes
<out>/v<N>/mask.txt: one line per row, one character per cell:

    W  lava    rivers, channels, pools, glowing fissure fields (GroundClass.Water)
    B  built   forge plates, pipework, ruins already painted in the tile
    O  open    smooth ash flats and cooled crust: ground that is not lava
    C  canopy  dense textured basalt / broken crust

Method (colour + edge statistics, no hand painting): per pixel luminance,
saturation, warmth (r - g), yellowness (g - b) and edge energy (|dx| + |dy|
of luminance), box-blurred over ~13 px (wrapping vertically: the tile
repeats), then per-variant scores ranked by percentile (the share of each
class is set per variant below, from looking at the tiles), a 16 px cell
takes the majority class of its pixels and a 3x3 mode filter (vertical
wrap) removes speckle. An overlay per variant is written beside the masks
when --debug DIR is given.

    python3 src~/ground_masks.py <Resources/Worlds/Ember/Backdrop3 (holds v1..v4)> [--debug DIR]
"""
import sys, os
import numpy as np
from PIL import Image

CELL = 16
CLASSES = "WBOC"

# per variant: score expressions and class shares (fractions of the tile)
# features (all blurred ~9 px): lum, sat, warm (r - g), yellow (g - b), tex (edge energy),
# grey (low saturation share), hot (bright saturated orange: lava)
VARIANTS = {
    # caldera fields: lava rivers and lakes through black basalt, a few forge plates
    1: dict(water=lambda f: f['hot'] * 3 + f['lum'] - f['tex'] * 2, wshare=.17, wvote=.35,
            built=lambda f: f['grey'] * 2 + f['tex'], bshare=.05, oshare=.12),
    # forge city: a grey plate maze of pipework, tanks and cooling towers; lava seeps between
    2: dict(water=lambda f: f['hot'] * 3 + f['yellow'] - f['tex'] * 2, wshare=.10, wvote=.35,
            built=lambda f: f['tex'] + f['grey'] * 2, bshare=.55, oshare=.08),
    # ash ridges: dark furrowed dunes with thin lava threads and ruined settlements
    3: dict(water=lambda f: f['hot'] * 3 + f['lum'] - f['tex'], wshare=.10, wvote=.3,
            built=lambda f: f['tex'] * 2 + f['grey'], bshare=.12, oshare=.22),
    # cracked crust: glowing fissures between black basalt plates, a crater rim
    4: dict(water=lambda f: f['hot'] * 3 + f['lum'] * 2, wshare=.12, wvote=.3,
            built=lambda f: f['tex'] + f['grey'], bshare=.04, oshare=.20),
}

def blur(a, r):
    # box blur, wrap vertically (the tile repeats), clamp horizontally
    a = np.concatenate([a[-r:], a, a[:r]], 0)
    c = np.cumsum(np.pad(a, ((1, 0), (0, 0))), 0)
    a = (c[2 * r + 1:] - c[:-2 * r - 1]) / (2 * r + 1)
    a = np.pad(a, ((0, 0), (r, r)), mode='edge')
    c = np.cumsum(np.pad(a, ((0, 0), (1, 0))), 1)
    return (c[:, 2 * r + 1:] - c[:, :-2 * r - 1]) / (2 * r + 1)

def features(im):
    lum = im @ np.array([.299, .587, .114])
    mx, mn = im.max(2), im.min(2)
    sat = (mx - mn) / np.maximum(mx, 1e-4)
    gx = np.abs(np.diff(lum, axis=1, append=lum[:, -1:]))       # no horizontal wrap (the tile does not)
    gx[:, -1] = gx[:, -2]
    gy = np.abs(np.roll(lum, -1, 0) - lum)
    f = dict(lum=lum, sat=sat, warm=im[..., 0] - im[..., 1], yellow=im[..., 1] - im[..., 2],
             tex=gx + gy, grey=(sat < .3).astype(np.float64),
             hot=((im.max(2) > .45) & (sat > .5)).astype(np.float64))
    return {k: blur(v, 4) for k, v in f.items()}

def rank(score):
    flat = score.ravel()
    order = flat.argsort().argsort()
    return (order / (flat.size - 1)).reshape(score.shape)

def classify(path, cfg):
    im = np.asarray(Image.open(path).convert('RGB')).astype(np.float64) / 255
    f = features(im)
    H, W = im.shape[:2]
    cls = np.full((H, W), 3, np.int8)                       # canopy
    tex_r = rank(f['tex'])
    cls[tex_r < cfg['oshare'] + cfg['wshare']] = 2          # smooth: open (water taken below)
    cls[rank(cfg['built'](f)) > 1 - cfg['bshare']] = 1
    cls[rank(cfg['water'](f)) > 1 - cfg['wshare']] = 0
    gh, gw = H // CELL, W // CELL
    votes = np.stack([(cls == k).reshape(gh, CELL, gw, CELL).mean((1, 3)) for k in range(4)], -1)
    grid = votes.argmax(-1)
    # thin rivers: a cell a good part water is water
    grid[votes[..., 0] >= cfg.get('wvote', .5)] = 0
    # 3x3 mode filter, vertical wrap
    for _ in range(2):
        cnt = np.zeros((gh, gw, 4))
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                sh = np.roll(grid, dy, 0)
                sh = np.roll(sh, dx, 1) if dx else sh
                if dx == 1: sh[:, 0] = grid[:, 0]
                if dx == -1: sh[:, -1] = grid[:, -1]
                for k in range(4): cnt[..., k] += (sh == k) * (2 if dx == 0 and dy == 0 else 1)
        grid = cnt.argmax(-1)
    return im, grid

def main():
    base = sys.argv[1]
    dbg = sys.argv[sys.argv.index('--debug') + 1] if '--debug' in sys.argv else None
    out = sys.argv[sys.argv.index('--out') + 1] if '--out' in sys.argv else base
    for v, cfg in VARIANTS.items():
        im, grid = classify(os.path.join(base, 'v%d' % v, 'mid.png'), cfg)
        os.makedirs(os.path.join(out, 'v%d' % v), exist_ok=True)
        with open(os.path.join(out, 'v%d' % v, 'mask.txt'), 'w') as fh:
            fh.write('\n'.join(''.join(CLASSES[c] for c in row) for row in grid) + '\n')
        share = [(grid == k).mean() for k in range(4)]
        print('v%d' % v, ' '.join('%s %.2f' % (CLASSES[k], share[k]) for k in range(4)))
        if dbg:
            cols = np.array([[0, 120, 255], [255, 60, 0], [255, 255, 0], [0, 160, 0]]) / 255.
            big = np.kron(grid, np.ones((CELL, CELL), int))
            ov = im * .5 + cols[big] * .5
            Image.fromarray((np.concatenate([im, ov], 1) * 255).astype(np.uint8)).save(os.path.join(dbg, 'mask_v%d.png' % v))

if __name__ == '__main__':
    main()
