#!/usr/bin/env python3
"""Affinity masks for the Desert ground tiles (GroundPlacement / GroundMask).

Round 5 (2026-11): the four ground tiles are painted 1024 x 2048 scenes that carry their OWN props (outposts, mining
rigs, rail tracks, bones, rock stacks, oases, palms, solar panels, crater rims, work fires). Everything the game pins
onto the ground (landmarks, pipes, ground features, launch sites / elite docks, worms and their wake, ambient loops)
reads this grid, so a painted prop must be BLOCKED: it is class C, which no dock, structure, hollow, dune or feature rule accepts.

For each variant's mid.png the grid is 32 x 64 cells (32 px each, row 0 = the TOP of the image), one character per cell,
written to <out>/v<N>/mask.txt. The letters are the engine's GroundClass names; for the Desert they mean:

    W  soft ground  open dune sand / crater dust with nothing painted on it (GroundClass.Water): the worms swim here
    O  hardpan      v2: open, smooth salt flat with nothing painted on it (GroundClass.Open)
    N / M  story ground  free ground right beside painted props (within NEAR cells of a C cell): beside the rails, rigs and brine pools (v2, M = hardpan),
                    the rock stacks, outpost and oasis (v3), the work fires and ruined blocks (v4), the outposts and bones (v1). N is soft ground (Water) with
                    the Near flag, M hardpan (Open) with it: the zone DesertTuning.StoryZone lets wrecks, machinery, landmarks, wells and features stand in
                    (AUTHORED PLACEMENT, round 5)
    T  trough       v1 only: a DUNE TROUGH (the shaded foot of a dune), soft ground (Water) with the Trough flag: half-buried wrecks go here, never on a crest
    C  blocked      a painted prop and a one-cell margin round it, brine / oasis water, rock stacks, crater rims
                    (GroundClass.Canopy)

Method: no hand painting. Per variant a detector flags the pixels that belong to a painted prop (colour distance from the
ground's median chromaticity, edge energy, local top-hat brightness, teal water, work-fire glow -- see the props_* functions); a cell with
>= the variant's share (VARIANTS) flagged pixels is C. The shares are tuned so every painted prop (and its skirt) is blocked while 9-60% of the
tile stays free for a docks-size footprint (v3 / v4 are crowded by design, so they ignore the smallest rubble). v1 / v3 / v4 ground
is soft (W), v2's flat is hardpan (O).  --debug DIR writes an overlay per variant.

    python3 src~/ground_masks.py <Resources/Worlds/Desert/Backdrop3 (holds v1..v4)> [--debug DIR]
"""
import sys, os
import numpy as np
from PIL import Image

CLASSES = "WBOCTNM"
GROW = 0                # cells of margin round every blocked cell: none -- the planner's footprints (~5 x 4 cells) already need a whole free patch


def box(a, r):
    k = 2 * r + 1
    a = np.concatenate([a[-r:], a, a[:r]], 0)                 # the tile repeats vertically
    c = np.cumsum(np.pad(a, ((1, 0), (0, 0))), 0)
    a = (c[k:] - c[:-k]) / k
    a = np.pad(a, ((0, 0), (r, r)), mode='edge')
    c = np.cumsum(np.pad(a, ((0, 0), (1, 0))), 1)
    return (c[:, k:] - c[:, :-k]) / k


def base_features(im):
    s = im.sum(2) + 1e-3
    ch = im / s[..., None]
    med = np.median(ch.reshape(-1, 3), 0)
    dist = np.sqrt(((ch - med) ** 2).sum(2))                  # chromaticity distance from the ground's own colour
    lum = im @ np.array([.299, .587, .114])
    gx = np.abs(np.diff(lum, axis=1, append=lum[:, -1:]))
    gy = np.abs(np.roll(lum, -1, 0) - lum)
    return dist, lum, gx + gy


def props_dunes(im):
    """v1 khaki dune sea: rust / steel outposts, off-white bones, debris -- everything that is not smooth sand."""
    dist, lum, edge = base_features(im)
    return (box(dist, 3) > .035) | (box(edge, 4) > .11)


def props_salt(im):
    """v2 lilac salt flat: rails, rigs, sleepers, brine pools (teal), rubble heaps."""
    dist, lum, edge = base_features(im)
    teal = (im[..., 1] - im[..., 0] > .04) & (im[..., 2] > im[..., 0] * .9)
    rubble = (box(lum, 3) - box(lum, 20)) > .07
    return (box(dist, 3) > .035) | (box(edge, 4) > .11) | (box(teal.astype(float), 3) > .3) | (box(rubble.astype(float), 3) > .5)


def props_oasis(im):
    """v3 dusk rock oasis: rock stacks / arches / mushroom rocks, the outpost with solar panels, the oasis, palms."""
    dist, lum, edge = base_features(im)
    return (box(dist, 3) > .10) | (box(edge, 4) > .085)


def props_craters(im):
    """v4 night craters: lit tops of the painted blocks / ruins / crater rims (brightness top-hat, the orange glow veins
    removed first), the teal windows and the red-hot work fires."""
    r, g, b = im[..., 0], im[..., 1], im[..., 2]
    lum = im @ np.array([.299, .587, .114])
    glow = (r - b > .18) & (r > .45)
    w = (~(box(glow.astype(float), 2) > 0)).astype(float)
    l2 = np.where(w > 0, lum, box(lum * w, 8) / np.maximum(box(w, 8), 1e-3))
    top = box(l2, 2) - box(l2, 18)
    f = box((top > .04).astype(float), 5) > .25
    teal = (g > b * .9) & (g > r * 1.1) & (g > .3)
    fire = (r > .8) & (g > .3) & (b < .4)
    return f | (box(teal.astype(float), 6) > .05) | (box(fire.astype(float), 8) > .12)


# per variant: the prop detector, and the class of the open ground left over
VARIANTS = {
    1: (props_dunes, 'W', .10),
    2: (props_salt, 'O', .10),
    3: (props_oasis, 'W', .25),
    4: (props_craters, 'W', .24),
}
NEAR = {1: 2, 2: 3, 3: 2, 4: 1}          # radius (cells) of the story ring round the blocked cells, per variant


def ring(blocked, d):
    out = blocked.copy()
    gw = blocked.shape[1]
    for dy in range(-d, d + 1):
        for dx in range(-d, d + 1):
            if dx * dx + dy * dy > d * d: continue
            sh = np.roll(blocked, dy, 0)
            if dx > 0: sh = np.pad(sh, ((0, 0), (dx, 0)))[:, :gw]
            elif dx < 0: sh = np.pad(sh, ((0, 0), (0, -dx)))[:, -dx:]
            out |= sh
    return out


def story(v, im, chars, cell):
    """Authored zones on the free ground: B beside props (all), and for v1 O = dune troughs (shaded, below the local mean)."""
    blocked = chars == 'C'
    near = ring(blocked, NEAR[v]) & ~blocked
    out = chars.copy()
    if v == 1:
        lum = im @ np.array([.299, .587, .114])
        tr = box(lum, 10) < box(lum, 64) - .025
        gh, gw = chars.shape
        trough = tr.reshape(gh, cell, gw, cell).mean((1, 3)) > .5
        out[trough & ~blocked] = 'T'
        out[near & ~trough] = 'N'
    elif v == 2:
        out[near] = 'M'
    else:
        out[near] = 'N'
    return out


def classify(path, detector, ground, cell_share):
    im = np.asarray(Image.open(path).convert('RGB')).astype(np.float64) / 255
    H, W = im.shape[:2]
    cell = H // 64                                            # always a 32 x 64 grid
    flag = detector(im)
    gh, gw = H // cell, W // cell
    share = flag.reshape(gh, cell, gw, cell).mean((1, 3))
    blocked = share >= cell_share
    grown = blocked.copy()
    for dy in range(-GROW, GROW + 1):
        for dx in range(-GROW, GROW + 1):
            sh = np.roll(blocked, dy, 0)                      # (rows wrap: the tile repeats)
            if dx > 0: sh = np.pad(sh, ((0, 0), (dx, 0)))[:, :gw]
            elif dx < 0: sh = np.pad(sh, ((0, 0), (0, -dx)))[:, -dx:]
            grown |= sh
    # fill the holes a prop's smooth top leaves (a free cell with 4+ blocked neighbours of its 8 is part of the prop)
    n = np.zeros(grown.shape, int)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dy == 0 and dx == 0: continue
            sh = np.roll(grown, dy, 0).astype(int)
            if dx > 0: sh = np.pad(sh, ((0, 0), (dx, 0)))[:, :gw]
            elif dx < 0: sh = np.pad(sh, ((0, 0), (0, -dx)))[:, -dx:]
            n += sh
    grown = grown | (n >= 4)
    chars = np.where(grown, 'C', ground)
    return im, flag, chars, cell


def zoned(v, im, chars, cell):
    return story(v, im, chars, cell)


def main():
    base = sys.argv[1]
    dbg = sys.argv[sys.argv.index('--debug') + 1] if '--debug' in sys.argv else None
    out = sys.argv[sys.argv.index('--out') + 1] if '--out' in sys.argv else base
    only = [int(x) for x in sys.argv[sys.argv.index('--only') + 1].split(',')] if '--only' in sys.argv else list(VARIANTS)
    for v, (det, ground, cs) in VARIANTS.items():
        if v not in only: continue
        im, flag, chars, cell = classify(os.path.join(base, 'v%d' % v, 'mid.png'), det, ground, cs)
        chars = zoned(v, im, chars, cell)
        os.makedirs(os.path.join(out, 'v%d' % v), exist_ok=True)
        with open(os.path.join(out, 'v%d' % v, 'mask.txt'), 'w') as fh:
            fh.write('\n'.join(''.join(row) for row in chars) + '\n')
        print('v%d' % v, ' '.join('%s %.2f' % (c, (chars == c).mean()) for c in CLASSES))
        if dbg:
            big = np.kron(chars == 'C', np.ones((cell, cell), bool))
            ov = im.copy()
            ov[big] = ov[big] * .5 + np.array((1, 0, 0)) * .5
            Image.fromarray((ov * 255).astype(np.uint8)).resize((512, 1024), Image.NEAREST).save(os.path.join(dbg, 'mask_v%d.png' % v))


if __name__ == '__main__':
    main()
