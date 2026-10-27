#!/usr/bin/env python3
"""Affinity masks for the Desert ground tiles (GroundPlacement / GroundMask).

For each variant's mid.png (the tile the landmarks are pinned to) this classifies the ground
into a coarse grid of 16 px cells (32 x 64 for the 512 x 1024 tile, row 0 = the TOP of the
image) and writes <out>/v<N>/mask.txt, one character per cell. The letters are the engine's
GroundClass names; for the Desert they mean:

    W  dune sand    soft drifted sand, most of every tile (GroundClass.Water): the worms swim
                    here; wells, ribs and wreckage may sit on it
    B  built        buried machine ruins, cracked-pan wreckage, rust-dark structure already
                    painted in the tile (GroundClass.Built)
    O  hardpan      smooth pale flats: salt, fused glass, canyon floor (GroundClass.Open):
                    heavy structures and launch sites stand here
    C  cliff / rock canyon walls, rock shelves, dark textured crust (GroundClass.Canopy)

Method (colour + edge statistics, no hand painting): per-pixel luminance, saturation, warmth
(r - b: rust) and edge energy (|dx| + |dy| of luminance) box-blurred ~9 px (wrapping
vertically: the tile repeats), then per-variant scores ranked by percentile (the share of
each class is set per variant below, from looking at the tiles); a 16 px cell takes the
majority class of its pixels and a 3x3 mode filter removes speckle. --debug DIR writes an
overlay per variant.

    python3 src~/ground_masks.py <Resources/Worlds/Desert/Backdrop3 (holds v1..v4)> [--debug DIR]
"""
import sys, os
import numpy as np
from PIL import Image

CELL = 16          # for the 512 x 1024 tiles; the painted 1024 x 2048 tiles use 32 (set per image below: always a 32 x 64 grid)
CLASSES = "WBOC"

# score expressions and class shares (fractions of the tile)
VARIANTS = {
    # day dunes (painted 1024 x 2048 tile): dark half-buried rock outcrops = cliff (C: no dock or structure on rock), the pale gravel
    # wadi and bleached wreck bits = hardpan (O: heavy structures and launch sites), everything else soft dune sand (W)
    1: dict(built=lambda f: f['tex'] * 2 + f['lum'] - f['sat'] * 3, bshare=.015,
            shoal=lambda f: f['lum'] - f['sat'] * 3 - f['tex'], oshare=.10, kelp=lambda f: f['tex'] * 4 - f['lum'] - f['sat'], cshare=.10, vote=.25),
    # canyon and ruins (painted 1024 x 2048 tile): every textured cell -- strata mesa walls, standing ruins, arches, columns, rubble heaps -- is
    # cliff (C: no dock or structure on rock or on a painted ruin), the smooth packed-sand floor and the cobbled wadi = hardpan (O: heavy
    # structures and launch sites), the rest soft sand (W); the oases (oasis_footprint.npy, +1 cell shore) are C as well (water)
    2: dict(built=lambda f: f['tex'] - f['lum'], bshare=.0,
            shoal=lambda f: -f['tex'] * 3 + f['lum'] * .5, oshare=.34, kelp=lambda f: f['tex'] * 3 + f['warm'] * .5, cshare=.36, vote=.30, oasis=True),
    # salt flat (painted 1024 x 2048 tile): busiest relief = built (crystal / rubble), smoothest crust = hardpan, no cliffs;
    # brine POOLS (teal) are class C so no dock or structure ever lands on water
    3: dict(built=lambda f: f['tex'], bshare=.18,
            shoal=lambda f: -f['tex'], oshare=.40, kelp=lambda f: -f['lum'], cshare=0, pool=True),
    # night dunes (painted 1024 x 2048 tile): as the day dunes -- dark rock outcrops and wreck piles = cliff (C), the pale gravel wadi = hardpan (O)
    4: dict(built=lambda f: f['tex'] * 2 + f['lum'] - f['sat'] * 3, bshare=.015,
            shoal=lambda f: f['lum'] - f['sat'] * 3 - f['tex'], oshare=.10, kelp=lambda f: f['tex'] * 4 - f['lum'] - f['sat'], cshare=.10, vote=.25),
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
    global CELL
    CELL = H // 64
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
    if cfg.get('pool'):
        # teal brine (g > r, b > .9 r): cells that are > 20% pool -> C
        teal = ((im[..., 1] - im[..., 0] > .04) & (im[..., 2] > im[..., 0] * .9)).astype(float)
        frac = teal.reshape(gh, CELL, gw, CELL).mean((1, 3))
        cd = frac > .2
        grid = np.where(cd, 3, grid)
    if cfg.get('oasis'):
        fp = os.path.join(os.path.dirname(path), 'oasis_footprint.npy')
        if os.path.exists(fp):
            o = np.load(fp)
            o = o.reshape(gh, o.shape[0] // gh, gw, o.shape[1] // gw).any((1, 3))     # 4 px cells -> mask cells
            od = o.copy()
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    od |= np.roll(np.roll(o, dy, 0), dx, 1)
            grid = np.where(od, 3, grid)
    return im, grid

def main():
    base = sys.argv[1]
    dbg = sys.argv[sys.argv.index('--debug') + 1] if '--debug' in sys.argv else None
    out = sys.argv[sys.argv.index('--out') + 1] if '--out' in sys.argv else base
    only = [int(x) for x in sys.argv[sys.argv.index('--only') + 1].split(',')] if '--only' in sys.argv else list(VARIANTS)
    for v, cfg in VARIANTS.items():
        if v not in only: continue
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
