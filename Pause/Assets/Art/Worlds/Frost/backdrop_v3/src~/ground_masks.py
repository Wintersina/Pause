#!/usr/bin/env python3
"""Affinity masks for the Frost ground tiles (GroundPlacement / GroundMask).

For each variant's mid.png (the tile the landmarks are pinned to) this classifies the ground
into a coarse grid of 16 px cells (32 x 64 for the 512 x 1024 tile, row 0 = the TOP of the
image) and writes <out>/v<N>/mask.txt, one character per cell:

    W  water      open water leads between the floes (v1), the harbour basins (v2), the frozen
                  ponds (v3), the crevasse depths (v4): GroundClass.Water. Icebreakers sail
                  here, the offshore rigs / platforms stand here on their own floe skirt,
                  a causeway spans it.
    B  built      piers, quays, roads, pipe racks, plants and rigs already painted in the tile:
                  nothing is dropped on top of them.
    O  open       smooth snow flats / pale shelf ice: land that is not painted industry.
    C  sheet      the ice sheets / pack ice / glacier plateaus / rock ridges: the textured
                  ground (GroundClass.Canopy).

Method (colour + edge statistics, no hand painting), as Tide's ground_masks.py: per-pixel
luminance, saturation, blue cast and edge energy box-blurred ~9 px (wrapping vertically: the
tile repeats), then per-variant scores ranked by percentile (the class shares are set per
variant below, from looking at the tiles); a 16 px cell takes the majority class and a 3x3
mode filter removes speckle. --debug DIR writes an overlay per variant.

    python3 src~/ground_masks.py <Resources/Worlds/Frost/Backdrop3 (holds v1..v4)> [--debug DIR]
"""
import sys, os
import numpy as np
from PIL import Image

CELL = 16
CLASSES = "WBOC"      # index 0 water, 1 built, 2 open, 3 sheet

VARIANTS = {
    # frozen ocean: dark leads between big ice sheets, a few painted derricks / rail trestles
    1: dict(water=lambda f: -f['lum'] - f['tex'] * .5, wshare=.34,
            built=lambda f: f['on_ice'] * 40 + f['dark_tex'], bshare=.07,
            open=lambda f: f['lum'] - f['tex'] * 2, oshare=.10),
    # coast + harbour: black basins, quays / piers / sheds, floe pack, a snowy headland
    2: dict(water=lambda f: -f['lum'] - f['tex'] * .5, wshare=.36,
            built=lambda f: f['dark_tex'], bshare=.24,
            open=lambda f: f['lum'] - f['tex'] * 2, oshare=.10),
    # inland tundra industry: frozen ponds, a dense grid of roads / pipes / plants, snow flats, ridges
    3: dict(water=lambda f: f['blue'] * 2 - f['tex'] * 2 - f['lum'] * .5, wshare=.13,
            built=lambda f: f['dark_tex'], bshare=.42,
            open=lambda f: f['lum'] - f['tex'] * 2, oshare=.25),
    # glacier / crevasse night: dark crevasse depths between flat-topped plateaus
    4: dict(water=lambda f: -f['lum'] - f['tex'] * .3, wshare=.30,
            built=lambda f: f['dark_tex'], bshare=.03,
            open=lambda f: f['lum'] - f['tex'] * 2, oshare=.20),
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
    tex = gx + gy
    f = dict(lum=lum, sat=sat, blue=im[..., 2] - im[..., 0], tex=tex)
    f = {k: blur(v, 4) for k, v in f.items()}
    # painted structure: fine dark-on-light line work (metal over snow) -- edge energy that
    # survives at a small scale but is not just the big floe outlines
    fine = blur(tex, 1) - blur(tex, 8)
    f['dark_tex'] = blur(np.abs(fine), 4) * 3 + f['tex'] - f['sat'] * .1
    # a dark structure standing on bright ice (derricks, trestles, plant): darker than its
    # surroundings at a small scale, inside a bright neighbourhood (open water is dark all round)
    f['on_ice'] = blur(np.maximum(blur(lum, 10) - lum, 0), 3) * blur(lum, 10)
    return f

def rank(score):
    flat = score.ravel()
    order = flat.argsort().argsort()
    return (order / (flat.size - 1)).reshape(score.shape)

def classify(path, cfg):
    im = np.asarray(Image.open(path).convert('RGB')).astype(np.float64) / 255
    f = features(im)
    H, W = im.shape[:2]
    cls = np.full((H, W), 3, np.int8)                    # sheet by default
    cls[rank(cfg['open'](f)) > 1 - cfg['oshare']] = 2
    cls[rank(cfg['built'](f)) > 1 - cfg['bshare']] = 1
    cls[rank(cfg['water'](f)) > 1 - cfg['wshare']] = 0
    gh, gw = H // CELL, W // CELL
    votes = np.stack([(cls == k).reshape(gh, CELL, gw, CELL).mean((1, 3)) for k in range(4)], -1)
    grid = votes.argmax(-1)
    # a cell a third covered by painted structure is built: nothing is dropped on it
    grid[votes[..., 1] >= cfg.get('bvote', .34)] = 1
    # a cell that is even 45% water reads as water (the floes' dark gaps are the point)
    grid[votes[..., 0] >= .45] = 0
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
            os.makedirs(dbg, exist_ok=True)
            cols = np.array([[0, 120, 255], [255, 60, 0], [255, 255, 0], [0, 200, 120]]) / 255.
            big = np.kron(grid, np.ones((CELL, CELL), int))
            lifted = np.minimum(1, im * 2.2)
            ov = lifted * .55 + cols[big] * .45
            Image.fromarray((np.concatenate([lifted, ov], 1) * 255).astype(np.uint8)).save(os.path.join(dbg, 'mask_v%d.png' % v))

if __name__ == '__main__':
    main()
