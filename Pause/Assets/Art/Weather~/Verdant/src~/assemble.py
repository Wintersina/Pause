#!/usr/bin/env python3
"""Assemble Verdant weather from the selected painted image-generation sources.

Writes new draft files in src~. Final assets are copied into the parent once verified.
All resize and coordinate transforms use nearest-neighbour sampling.
"""
from pathlib import Path
import sys

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
PALETTE = np.array([
    (15, 58, 36), (25, 76, 43), (36, 96, 50), (52, 121, 60),
    (78, 153, 69), (117, 188, 88), (182, 245, 138), (232, 255, 224),
    (68, 55, 37), (94, 74, 42), (125, 102, 54),
    (124, 119, 56), (171, 161, 75), (211, 195, 101), (246, 226, 122),
], dtype=np.uint8)
ALPHA_LEVELS = np.array([0, 32, 48, 64, 80, 96, 112, 128, 144, 160, 176], dtype=np.uint8)


def rgba(path):
    return np.asarray(Image.open(HERE / path).convert('RGBA')).copy()


def nearest(a, wh):
    return np.asarray(Image.fromarray(a).resize(wh, Image.Resampling.NEAREST)).copy()


def clean(a, strength=1.0, cutoff=25, ceiling=176):
    """Remove generated halos, quantise material ramps and stepped air alpha."""
    a = a.copy()
    alpha = a[:, :, 3].astype(np.float32)
    alpha[alpha < cutoff] = 0
    alpha = np.minimum(alpha * strength, ceiling)
    ai = np.abs(alpha[:, :, None] - ALPHA_LEVELS[None, None, :]).argmin(2)
    a[:, :, 3] = ALPHA_LEVELS[ai]
    a[a[:, :, 3] == 0, :3] = 0
    m = a[:, :, 3] > 0
    rgb = a[m, :3].astype(np.int32)
    # Weighted colour distance keeps the green and pale-gold material families.
    d = (rgb[:, None, :] - PALETTE[None, :, :].astype(np.int32)) ** 2
    d = (d * np.array([2, 2, 1])[None, None, :]).sum(2)
    a[m, :3] = PALETTE[d.argmin(1)]
    return a


def seamless_band(a, overlap=28):
    """A short wrap blend makes the generated cross-section periodic at x=0/511."""
    a = a.astype(np.float32)
    left = a[:, :overlap].copy()
    right = a[:, -overlap:].copy()
    for j in range(overlap):
        t = (j + 1) / (overlap + 1)
        # Edge pixels converge toward the same mixed source, including alpha.
        mix = (left[:, j] + right[:, j]) * .5
        a[:, j] = mix * (1 - t) + left[:, j] * t
        a[:, -overlap + j] = right[:, j] * (1 - t) + mix * t
    edge = ((a[:, 0] + a[:, -1]) * .5).astype(np.uint8)
    a[:, 0] = edge
    a[:, -1] = edge
    return clean(a.astype(np.uint8), 1, 0)


def gust_source():
    src = rgba('gust_selected.png')
    # Crop inside the two natural ends so edge material reaches the tile seam.
    src = src[100:625, 70:2102]
    band = nearest(src, (512, 306))
    cell = np.zeros((512, 512, 4), np.uint8)
    cell[103:409] = band
    cell = clean(cell, .72, 36, 160)
    return seamless_band(cell)


def updraft_source():
    src = rgba('updraft_selected.png')[8:1528, 200:850]
    src = nearest(src, (302, 486))
    cell = np.zeros((512, 512, 4), np.uint8)
    cell[13:499, 105:407] = src
    return clean(cell, .75, 38, 168)


def warning(base, phase):
    """Keep sparse pieces of the same painted weather, with a thin preview."""
    a = base.copy()
    yy, xx = np.indices((512, 512))
    # Spatially stable 3-pixel islands avoid per-pixel static in a timed warning.
    noise = ((xx // 3 * 37 + yy // 3 * 71 + (xx // 17) * 11 + phase * 19) % 101)
    keep = noise < (34 if phase == 0 else 38)
    a[~keep, 3] = 0
    a[:, :, 3] = np.where(a[:, :, 3] > 0,
                          np.maximum(32, (a[:, :, 3].astype(np.float32) * .68).astype(np.uint8)), 0)
    # Warning cells are not fronts; retain their required clear cell margins.
    a[:, :8] = 0; a[:, -8:] = 0
    return a


def make_hazard():
    sheet = np.zeros((1536, 3072, 4), np.uint8)
    gust = gust_source()
    up = updraft_source()
    yy = np.arange(512)[:, None]
    xx = np.arange(512)[None, :]
    gust_offsets = [0, 7, 13, 18, 12, 5]
    for k in range(6):
        # Six distinct poses, with the last only five pixels from the first.
        shift = gust_offsets[k]
        frame_a = np.roll(gust, shift, axis=1)
        bright = (frame_a[:, :, 0] >= 170) & (frame_a[:, :, 3] > 0)
        twinkle = 1 + .13 * np.sin(xx / 21 + yy / 17 + 2 * np.pi * k / 6)
        frame_a[:, :, 3] = np.where(bright,
                                     np.minimum(176, (frame_a[:, :, 3] * twinkle).astype(np.uint8)),
                                     frame_a[:, :, 3])
        # A second travelling wave shifts the vines and specks up the spiral.
        phase = 2 * np.pi * k / 6
        dx = np.rint(7 * np.sin(yy / 54 + phase)).astype(int)
        dy = np.rint(4 * np.cos(xx / 64 + phase)).astype(int)
        frame_b = up[(yy - dy) % 512, (xx - dx) % 512].copy()
        # Keep the column clear at the cell boundary after the nearest warp.
        frame_b[:7] = 0; frame_b[-7:] = 0
        frame_b[:, :7] = 0; frame_b[:, -7:] = 0
        sheet[0:512, k * 512:(k + 1) * 512] = frame_a
        sheet[512:1024, k * 512:(k + 1) * 512] = frame_b
    for k in range(2):
        sheet[1024:1536, k * 512:(k + 1) * 512] = warning(gust, k)
        sheet[1024:1536, (k + 2) * 512:(k + 3) * 512] = warning(up, k)
    return sheet


def crop_particle(src, center, radius):
    x, y = center
    rx, ry = radius
    return src[y - ry:y + ry, x - rx:x + rx]


def make_ambient():
    src = rgba('ambient_source.png')
    # Four painted variants of each kind are present in source rows.
    centers = [
        (166, 299), (314, 299), (454, 299), (592, 299),
        (901, 300), (1044, 298), (1224, 305), (1407, 300),
        (138, 730), (312, 743), (482, 733), (651, 731),
        (895, 718), (1060, 718), (1240, 728), (1413, 728),
    ]
    radii = [(56, 65)] * 4 + [(93, 103)] * 4 + [(94, 105)] * 4 + [(83, 90)] * 4
    targets = [72] * 4 + [103] * 4 + [96] * 4 + [85] * 4
    sheet = np.zeros((512, 2048, 4), np.uint8)
    for i, (center, radius, target) in enumerate(zip(centers, radii, targets)):
        part = crop_particle(src, center, radius)
        part = clean(part, .72, 75, 128)
        y, x = np.nonzero(part[:, :, 3] > 0)
        if len(x) == 0:
            raise ValueError(f'empty ambient source {i}')
        part = part[y.min():y.max()+1, x.min():x.max()+1]
        scale = min(target / part.shape[0], target / part.shape[1])
        part = nearest(part, (max(1, round(part.shape[1] * scale)), max(1, round(part.shape[0] * scale))))
        # A single airy specimen or cluster stays centred in its cell.
        row, col = divmod(i, 8)
        ox = col * 256 + (256 - part.shape[1]) // 2
        oy = row * 256 + (256 - part.shape[0]) // 2
        sheet[oy:oy + part.shape[0], ox:ox + part.shape[1]] = part
    return sheet


def save_new(name, pixels):
    path = HERE / name
    if path.exists():
        raise FileExistsError(path)
    Image.fromarray(pixels, 'RGBA').save(path)
    print(path)


if __name__ == '__main__':
    suffix = sys.argv[1] if len(sys.argv) > 1 else 'draft1'
    save_new(f'hazard_{suffix}.png', make_hazard())
    save_new(f'ambient_{suffix}.png', make_ambient())
