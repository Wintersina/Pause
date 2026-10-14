#!/usr/bin/env python3
"""Assemble Desert weather from the selected generated paintings.

All scaling uses nearest-neighbour. The painted sources are kept beside this
script so that the colour and transparency cleanup remains reproducible.
"""
from pathlib import Path
import numpy as np
from PIL import Image

SRC = Path(__file__).parent
OUT = SRC.parent
NEAR = Image.Resampling.NEAREST
PALETTE = np.array([(58, 38, 22), (110, 74, 34), (176, 122, 58),
                    (240, 213, 140), (255, 242, 196)], dtype=np.uint8)


def clean(im, alpha_scale=1.0, remove_red=False):
    a = np.asarray(im.convert('RGBA')).copy()
    rgb = a[:, :, :3].astype(np.float32)
    lum = rgb[:, :, 0] * .30 + rgb[:, :, 1] * .59 + rgb[:, :, 2] * .11
    # Keep the generated forms and highlights while snapping painterly tones
    # to the small Desert material ramp.
    idx = np.searchsorted([68, 111, 159, 209], lum)
    a[:, :, :3] = PALETTE[idx]
    if remove_red:
        red = (rgb[:, :, 0] > 100) & (rgb[:, :, 0] > rgb[:, :, 1] * 1.45) & (rgb[:, :, 0] > rgb[:, :, 2] * 1.45)
        a[:, :, 3][red] = 0
    alpha = np.minimum(174, a[:, :, 3].astype(np.float32) * alpha_scale)
    a[:, :, 3] = np.where(alpha < 10, 0, np.round(alpha / 12) * 12).clip(0, 180).astype(np.uint8)
    a[:, :, :3][a[:, :, 3] == 0] = 0
    return Image.fromarray(a, 'RGBA')


def front_source():
    im = Image.open(SRC / 'candidate_a1.png').convert('RGBA')
    im = im.resize((512, 290), NEAR)
    a = np.asarray(clean(im, .58)).copy()
    # Stitch the left and right texture while preserving the source painting.
    for x in range(72):
        t = (72 - x) / 144
        left, right = a[:, x].copy(), a[:, 511-x].copy()
        a[:, x] = (left * (1-t) + right * t).astype(np.uint8)
        a[:, 511-x] = (right * (1-t) + left * t).astype(np.uint8)
    a[:, -1] = a[:, 0]
    return a


def front_frame(base, k):
    # Six equal phase steps; the return to zero is the same amount of travel.
    phase = k * 512 / 6
    a = np.zeros((512, 512, 4), np.uint8)
    for sy in range(base.shape[0]):
        billow = int(round(7 * np.sin(2*np.pi*k/6 + sy/45)))
        dx = int(round(phase + billow))
        a[111 + sy] = np.roll(base[sy], dx, axis=0)
    a[:, -1] = a[:, 0]
    return Image.fromarray(a, 'RGBA')


def dust_source():
    im = Image.open(SRC / 'candidate_b1.png').convert('RGBA')
    im = im.crop((82, 60, 1190, 1180)).resize((368, 372), NEAR)
    im = clean(im, .57)
    canvas = Image.new('RGBA', (512, 512))
    canvas.paste(im, ((512-im.width)//2, (512-im.height)//2))
    # The generated spiral is nearly threefold. Average its 120-degree copies
    # to make the sixth frame flow into the first at the same angular rate.
    copies = [np.asarray(canvas.rotate(deg, resample=NEAR)).astype(float)
              for deg in (0, 120, 240)]
    mixed = np.mean(copies, axis=0)
    mixed[:, :, 3] = np.round(mixed[:, :, 3] / 12) * 12
    mixed[:, :, :3] = PALETTE[np.searchsorted([68, 111, 159, 209],
        mixed[:, :, 0]*.30 + mixed[:, :, 1]*.59 + mixed[:, :, 2]*.11)]
    mixed[:, :, :3][mixed[:, :, 3] < 10] = 0
    return Image.fromarray(mixed.clip(0, 255).astype(np.uint8), 'RGBA')


def warning(body, seed):
    a = np.asarray(body).copy()
    rng = np.random.default_rng(seed)
    # Stable 3-pixel clusters leave fragments of the same weather silhouette.
    small = rng.random(((a.shape[0]+2)//3, (a.shape[1]+2)//3)) < .37
    keep = np.repeat(np.repeat(small, 3, 0), 3, 1)[:a.shape[0], :a.shape[1]]
    a[:, :, 3] = np.where(keep, np.round(a[:, :, 3].astype(float)*.67), 0).astype(np.uint8)
    a[:, :6, 3] = 0
    a[:, -6:, 3] = 0
    a[:, :, :3][a[:, :, 3] == 0] = 0
    return Image.fromarray(a, 'RGBA')


def build_hazard():
    atlas = Image.new('RGBA', (3072, 1536))
    a = front_source()
    b = dust_source()
    for k in range(6):
        atlas.paste(front_frame(a, k), (512*k, 0))
        # The source has three arms. A 120-degree turn closes its visual loop.
        frame = b.rotate(k*20, resample=NEAR)
        atlas.paste(frame, (512*k, 512))
    atlas.paste(warning(front_frame(a, 0), 170), (0, 1024))
    atlas.paste(warning(front_frame(a, 2), 171), (512, 1024))
    atlas.paste(warning(b.rotate(0, resample=NEAR), 172), (1024, 1024))
    atlas.paste(warning(b.rotate(60, resample=NEAR), 173), (1536, 1024))
    atlas.save(OUT / 'hazard.png')


PARTICLES = [
    # Small generated sand grains and their motion tails.
    ((100, 70, 210, 153), (51, 42)), ((313, 132, 410, 209), (50, 42)),
    ((1002, 83, 1105, 158), (51, 42)), ((1115, 441, 1205, 512), (50, 43)),
    # Painted wind wisps; red generation artifacts are removed by clean().
    ((620, 91, 972, 289), (98, 55)), ((116, 186, 362, 367), (83, 60)),
    ((765, 585, 1055, 813), (98, 75)), ((325, 708, 550, 871), (86, 62)),
    # Painted tumbling pebbles.
    ((523, 53, 633, 188), (43, 51)), ((1062, 182, 1187, 309), (49, 49)),
    ((436, 344, 533, 435), (42, 39)), ((112, 537, 243, 660), (49, 46)),
    # Dry chaff and straw fragments.
    ((1075, 781, 1206, 973), (59, 75)), ((773, 667, 904, 775), (62, 51)),
    ((302, 457, 450, 550), (68, 43)), ((323, 924, 503, 1050), (75, 54)),
]


def build_ambient():
    source = Image.open(SRC / 'candidate_ambient.png').convert('RGBA')
    atlas = Image.new('RGBA', (2048, 512))
    for i, (box, size) in enumerate(PARTICLES):
        tile = source.crop(box).resize(size, NEAR)
        tile = clean(tile, .65 if i < 4 else .57, remove_red=True)
        cell = Image.new('RGBA', (256, 256))
        cell.paste(tile, ((256-size[0])//2, (256-size[1])//2))
        atlas.paste(cell, ((i % 8)*256, (i // 8)*256))
    atlas.save(OUT / 'ambient.png')


if __name__ == '__main__':
    build_hazard()
    build_ambient()
