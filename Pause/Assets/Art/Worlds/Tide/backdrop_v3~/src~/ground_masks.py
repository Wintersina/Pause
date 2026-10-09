#!/usr/bin/env python3
"""Affinity masks for the Tide ground tiles (GroundPlacement / GroundMask).

For each variant's mid.png (the tile the landmarks are pinned to) this classifies the ground
into a coarse grid of 16 px cells (32 x 64 for the 512 x 1024 tile, row 0 = the TOP of the
image) and writes <out>/v<N>/mask.txt, one character per cell:

    W  open water  most of every tile: swell, whitecaps, swirls (GroundClass.Water): vessels
                   float here, whirlpools and slicks lie here
    B  built       rig decks, walkways, pipework, the drowned city's streets and roofs
                   already painted in the tile: platforms, stacks and refineries stand here
    O  shallows    sandbars, reef flats, pale shoal water: reef heads, wrecks, vents
    C  canopy      kelp / dense dark textured growth

Method (colour + edge statistics, no hand painting): per-pixel luminance, saturation, warmth
(r - b: rust), mint (g - r) and edge energy (|dx| + |dy| of luminance) box-blurred ~9 px
(wrapping vertically: the tile repeats), then per-variant scores ranked by percentile (the
share of each class is set per variant below, from looking at the tiles); a 16 px cell takes
the majority class of its pixels and a 3x3 mode filter removes speckle. --debug DIR writes an
overlay per variant.

    python3 src~/ground_masks.py <Resources/Worlds/Tide/Backdrop3 (holds v1..v4)> [--debug DIR]
"""
import sys, os
import numpy as np
from PIL import Image

CELL = 16
CLASSES = "WBOC"

# score expressions and class shares (fractions of the tile)
VARIANTS = {
    # open sea: all swell; a few buoys (built) and the pale foam-rich shallows
    1: dict(built=lambda f: f['warm'] * 4 + f['tex'], bshare=.03,
            shoal=lambda f: f['lum'] * 2 - f['tex'], oshare=.12, kelp=lambda f: -f['lum'], cshare=.05),
    # rig field: rusted decks and walkways (warm) over dark water
    2: dict(built=lambda f: f['warm'] * 6 + f['tex'] * .5, bshare=.34,
            shoal=lambda f: f['lum'] * 2 - f['tex'], oshare=.06, kelp=lambda f: -f['lum'], cshare=.03),
    # drowned city: streets, roofs and tower crowns (lit, structured, grey-teal)
    3: dict(built=lambda f: f['tex'] * 2 + f['lum'] - f['sat'], bshare=.34,
            shoal=lambda f: f['lum'] * 2 - f['tex'], oshare=.12, kelp=lambda f: -f['lum'], cshare=.03),
    # night: swirling bioluminescent currents, a few lit platforms and a reef line
    4: dict(built=lambda f: f['warm'] * 6 + f['tex'], bshare=.04,
            shoal=lambda f: f['lum'] * 2 - f['tex'], oshare=.12, kelp=lambda f: -f['lum'], cshare=.05),
}

def blur(a, r):
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
    gx = np.abs(np.diff(lum, axis=1, append=lum[:, -1:]))
    gx[:, -1] = gx[:, -2]
    gy = np.abs(np.roll(lum, -1, 0) - lum)
    f = dict(lum=lum, sat=sat, warm=np.clip(im[..., 0] - im[..., 2], 0, 1), mint=im[..., 1] - im[..., 0], tex=gx + gy)
    return {k: blur(v, 4) for k, v in f.items()}

def rank(score):
    flat = score.ravel()
    order = flat.argsort().argsort()
    return (order / (flat.size - 1)).reshape(score.shape)

def classify(path, cfg):
    im = np.asarray(Image.open(path).convert('RGB')).astype(np.float64) / 255
    f = features(im)
    H, W = im.shape[:2]
    cls = np.zeros((H, W), np.int8)                      # water
    cls[rank(cfg['kelp'](f)) > 1 - cfg['cshare']] = 3
    cls[rank(cfg['shoal'](f)) > 1 - cfg['oshare']] = 2
    cls[rank(cfg['built'](f)) > 1 - cfg['bshare']] = 1
    gh, gw = H // CELL, W // CELL
    votes = np.stack([(cls == k).reshape(gh, CELL, gw, CELL).mean((1, 3)) for k in range(4)], -1)
    # water is the default: a cell takes a minority class when enough of it is that class
    grid = np.zeros((gh, gw), np.int64)
    for k in (3, 2, 1):
        grid[votes[..., k] >= cfg.get('vote', .4)] = k
    for _ in range(1):
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
