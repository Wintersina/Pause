"""Build the Verdant rail mine atlas from four generated pixel paintings.

All composition is at 64 pixels per cell. The only export resize is x2 NEAREST.
"""
from __future__ import annotations

from pathlib import Path
from collections import deque

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
OUT = HERE.parent
S = 64
N = Image.Resampling.NEAREST
INK = (7, 20, 12)
GREENS = np.array([
    INK, (13, 45, 28), (18, 75, 44), (24, 109, 62),
    (35, 146, 80), (72, 193, 116), (162, 235, 190),
], dtype=np.uint8)
PINKS = np.array([(105, 25, 78), (185, 38, 143), (255, 79, 216),
                  (255, 138, 230), (255, 224, 248)], dtype=np.uint8)
MINT = np.array([(167, 237, 192), (178, 255, 206), (187, 255, 215)], dtype=np.uint8)


def source(name: str, threshold: int = 120) -> Image.Image:
    im = Image.open(HERE / name).convert("RGBA")
    a = np.array(im)
    # The generator's semi-transparent backdrop glow is intentionally discarded.
    a[:, :, 3] = np.where(a[:, :, 3] >= threshold, 255, 0).astype(np.uint8)
    im = Image.fromarray(a, "RGBA")
    return im.crop(im.getbbox())


def exposed_edge(mask: np.ndarray) -> np.ndarray:
    """Find the outside contour while leaving painted pinholes alone."""
    h, w = mask.shape
    outside = np.zeros((h, w), dtype=bool)
    q = deque()
    for x in range(w):
        q.append((0, x)); q.append((h - 1, x))
    for y in range(h):
        q.append((y, 0)); q.append((y, w - 1))
    while q:
        y, x = q.popleft()
        if not (0 <= y < h and 0 <= x < w) or mask[y, x] or outside[y, x]:
            continue
        outside[y, x] = True
        q.extend(((y-1, x), (y+1, x), (y, x-1), (y, x+1)))
    exposed = np.array(Image.fromarray(outside.astype('uint8') * 255).filter(ImageFilter.MaxFilter(3))) > 0
    return mask & exposed


def palette(im: Image.Image, *, fade: float = 1, rim: bool = True) -> Image.Image:
    a = np.array(im.convert("RGBA"))
    rgb = a[:, :, :3].astype(np.float32) / 255
    hi = rgb.max(2)
    lo = rgb.min(2)
    sat = (hi - lo) / np.maximum(hi, .001)
    r, g, b = rgb.transpose(2, 0, 1)
    # Generated red fringe is assigned to the hostile pink ramp; violet is also pink.
    pink = (r > g * 1.5) & (b > g * 1.20) & (hi > .26)
    mint = (hi > .67) & (sat < .34) & ~pink
    value = (.22 * r + .70 * g + .08 * b)
    green_i = np.digitize(value, [.095, .19, .29, .41, .56, .72])
    pink_i = np.digitize(hi, [.29, .50, .75, .91])
    mint_i = np.digitize(hi, [.76, .91])
    out = np.zeros_like(a)
    out[:, :, :3] = GREENS[green_i]
    out[:, :, :3][pink] = PINKS[pink_i[pink]]
    out[:, :, :3][mint] = MINT[mint_i[mint]]
    mask = a[:, :, 3] >= 128
    out[:, :, 3] = (mask * round(255 * fade)).astype(np.uint8)
    if rim:
        edge = exposed_edge(mask)
        out[:, :, :3][edge] = PINKS[2]
        # The dark ink remains directly inside the hot rim on the forest material.
        inside = np.array(Image.fromarray(edge.astype('uint8') * 255).filter(ImageFilter.MaxFilter(3))) > 0
        inkline = inside & mask & ~edge & ~pink & ~mint
        out[:, :, :3][inkline] = INK
    return Image.fromarray(out, 'RGBA')


def crop_beam(im: Image.Image, trim_top: float, trim_bottom: float) -> Image.Image:
    w, h = im.size
    return im.crop((0, int(h * trim_top), w, int(h * trim_bottom)))


def put(native: Image.Image, art: Image.Image, col: int, row: int, y: int | None = None) -> None:
    cell_h = S * (3 if row == 0 else 1)
    if y is None:
        y = (cell_h - art.height) // 2
    native.alpha_composite(art, (col * S + (S - art.width) // 2, row * S + y))


def specks(cell: Image.Image, seed: int, n: int, y0: int = 4, y1: int = 60) -> None:
    rng = np.random.default_rng(seed)
    d = ImageDraw.Draw(cell)
    for _ in range(n):
        x = int(rng.integers(7, 57))
        y = int(rng.integers(y0, y1))
        d.point((x, y), fill=(255, 224, 248, 255) if rng.random() < .45 else (255, 79, 216, 255))


def build() -> None:
    braid_a = source('candidate_braid_a.png')
    braid_b = source('candidate_braid_b.png')
    burst = source('candidate_burst.png')
    pod = source('candidate_pod.png')
    native = Image.new('RGBA', (384, 384))

    # Alternate two independently painted coil phases. Changing source windows,
    # reflections and scale makes each phase a different thorn/leaf arrangement.
    phases = [
        (braid_a, .00, 1.00, 39, False),
        (braid_b, .00, 1.00, 43, False),
        (braid_a, .07, .96, 42, True),
        (braid_b, .08, .94, 39, True),
        (braid_a, .02, .90, 44, False),
        (braid_b, .03, .90, 41, True),
    ]
    for i, (src, top, bottom, width, flip) in enumerate(phases):
        sample = crop_beam(src, top, bottom).resize((width, 183), N)
        if flip:
            sample = sample.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        art = palette(sample)
        body = Image.new('RGBA', (S, S * 3))
        body.alpha_composite(art, ((S-width)//2, 4))
        rng = np.random.default_rng(510 + i)
        d = ImageDraw.Draw(body)
        for _ in range(11):
            yy = int(rng.integers(8, 184))
            xx = int(rng.choice([rng.integers(9, 14), rng.integers(50, 55)]))
            d.point((xx, yy), fill=(255, 224, 248, 255) if rng.random() < .4 else (255, 79, 216, 255))
        put(native, body, i, 0, 0)

    # Charge: the shell swells toward the mine muzzle's firing position.
    for i, (w, h) in enumerate(((14, 19), (19, 27), (24, 36), (29, 46))):
        cell = Image.new('RGBA', (S, S))
        art = palette(pod.resize((w, h), N))
        cell.alpha_composite(art, ((S-w)//2, 59-h))
        specks(cell, 121+i, i+2, max(4, 54-h), 57)
        put(native, cell, i, 3, 0)

    # Harmless seed-spore aim line. Each of the two phases tiles vertically.
    for i in range(2):
        cell = Image.new('RGBA', (S, S))
        d = ImageDraw.Draw(cell)
        for j in range(8):
            y = j * 8 + i * 4
            for dx, dy, color in ((0, 0, (222,255,231,220)),
                                  (-1, 1, (255,79,216,200)),
                                  (1, 2, (35,146,80,210))):
                if 0 <= y+dy < S:
                    d.point((32+dx, y+dy), fill=color)
        put(native, cell, 4+i, 3, 0)

    # Firing splay: different crops and flips expose different painted tendrils.
    for i, (w, h, mirror) in enumerate(((43, 45, False), (51, 51, True),
                                       (55, 54, False), (49, 50, True))):
        art = burst.resize((w, h), N)
        if mirror:
            art = art.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        cell = Image.new('RGBA', (S, S))
        cell.alpha_composite(palette(art), ((S-w)//2, 59-h))
        specks(cell, 710+i, 4+i, 5, 56)
        put(native, cell, i, 4, 0)

    # At the far rail the painted tendrils fan outward, then wither.
    for i, (w, h, fade) in enumerate(((54, 39, 1.), (49, 34, .82), (43, 27, .60))):
        art = burst.resize((w, h), N)
        if i == 1:
            art = art.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        cell = Image.new('RGBA', (S, S))
        cell.alpha_composite(palette(art, fade=fade), ((S-w)//2, 59-h))
        specks(cell, 811+i, 5-i, 10, 52)
        if i < 2:
            put(native, cell, 4+i, 4, 0)
        else:
            put(native, cell, 0, 5, 0)

    # Spore veil sampled from painted particles, with a soft alpha falloff.
    for i in range(2):
        art = burst.resize((43, 47), N)
        if i:
            art = art.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        a = np.array(palette(art, fade=.23, rim=False))
        yy, xx = np.mgrid[:47, :43]
        taper = np.minimum.reduce((xx/8, (42-xx)/8, yy/8, (46-yy)/8, np.ones_like(xx)))
        a[:, :, 3] = (a[:, :, 3] * np.clip(taper, 0, 1)).astype(np.uint8)
        put(native, Image.fromarray(a, 'RGBA'), 1+i, 5)

    atlas = native.resize((768, 768), N)
    atlas.save(OUT / 'verdant_attack_minebeam.png')
    preview(atlas)


def preview(atlas: Image.Image) -> None:
    contact = Image.new('RGBA', (1536, 1536), (11, 11, 26, 255))
    contact.alpha_composite(atlas.resize((1536, 1536), N))
    canvas = Image.new('RGB', (1536, 1830), '#0b0b1a')
    canvas.paste(contact.convert('RGB'))
    beam = atlas.crop((128, 0, 256, 384)).resize((40, 120), N)
    hit = atlas.crop((512, 512, 640, 640)).resize((60, 60), N)
    for i, bg in enumerate(('#0b0b1a', '#769461')):
        tile = Image.new('RGBA', (640, 210), bg)
        tile.alpha_composite(beam, (166, 42))
        tile.alpha_composite(hit, (360, 92))
        canvas.paste(tile.convert('RGB'), (90+706*i, 1590))
    canvas.save(OUT / 'preview.png')
    frames = []
    for i in range(6):
        panel = Image.new('RGBA', (440, 420), (11, 11, 26, 255))
        panel.alpha_composite(atlas.crop((i*128, 0, (i+1)*128, 384)))
        for j, (row, col, x, y, size) in enumerate(((3, i%4, 153, 176, 76),
                                                   (4, i%4, 246, 174, 76),
                                                   (3, 4+i%2, 355, 68, 65),
                                                   (4, 4+i%2, 351, 175, 75),
                                                   (5, 1+i%2, 352, 290, 75))):
            patch = atlas.crop((col*128, row*128, (col+1)*128, (row+1)*128))
            panel.alpha_composite(patch.resize((size, size), N), (x, y))
        frames.append(panel.convert('RGB'))
    # GIF stores centiseconds: 80+80+90+80+80+90 ms is exactly 12 fps on average.
    frames[0].save(OUT / 'preview.gif', save_all=True, append_images=frames[1:],
                   duration=[80, 80, 90, 80, 80, 90], loop=0, optimize=False)


if __name__ == '__main__':
    build()
