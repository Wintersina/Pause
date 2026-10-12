"""Build Storm's painted lightning rail-mine atlas at 64 px per cell.

The six bolt paths and all larger effects are separate samples from two generated
paintings. The final export is one exact x2 nearest-neighbour enlargement.
"""
from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
OUT = HERE.parent
S = 64
N = Image.Resampling.NEAREST
INK = np.array((7, 10, 24), dtype=np.uint8)
SLATE = np.array([(7, 10, 24), (25, 36, 54), (53, 75, 98),
                  (105, 137, 164), (167, 193, 211), (214, 234, 247)], dtype=np.uint8)
YELLOW = np.array([(93, 101, 104), (153, 161, 145), (210, 216, 178),
                   (245, 240, 179), (250, 249, 222)], dtype=np.uint8)
WHITE = np.array((252, 253, 250), dtype=np.uint8)
PINK = np.array([(255, 79, 216), (255, 138, 230), (255, 224, 248)], dtype=np.uint8)


def expanded(mask: np.ndarray) -> np.ndarray:
    return np.array(Image.fromarray(mask.astype('uint8') * 255).filter(ImageFilter.MaxFilter(3))) > 0


def contracted(mask: np.ndarray) -> np.ndarray:
    return np.array(Image.fromarray(mask.astype('uint8') * 255).filter(ImageFilter.MinFilter(3))) > 0


def paint(src: Image.Image, box: tuple[int, int, int, int], size: tuple[int, int],
          phase: int, *, rim: bool = True) -> Image.Image:
    """Isolate the painted electricity, then remap it to Storm's safe ramps."""
    a = np.array(src.crop(box).resize(size, N).convert('RGBA'))
    rgb = a[:, :, :3].astype(np.float32)
    r, g, b = rgb.transpose(2, 0, 1)
    bright = np.max(rgb, axis=2)
    white = (np.min(rgb, axis=2) > 188) & (bright > 216)
    yellow = (r > b * 1.09) & (g > b * 1.05) & (r > 105)
    blue = (b > r * 1.09) & (b > g * 1.025) & (b > 65)
    pink_source = (r > g * 1.18) & (b > g * 1.08) & (r > 82)
    # The generator painted a broad pink aura. Only painted electric cores and
    # their immediate colored contour survive, so the bolt stays forked.
    seed = (a[:, :, 3] >= 145) & (white | yellow | blue)
    near = expanded(seed)
    mask = seed | ((a[:, :, 3] >= 190) & near & pink_source)
    mask |= (a[:, :, 3] >= 205) & near & (bright < 87)
    value = .24 * r + .59 * g + .17 * b
    levels = np.digitize(value, [33, 68, 105, 145, 190]).clip(0, 5)
    out = np.zeros_like(a)
    out[:, :, :3] = SLATE[levels]
    ylevel = np.digitize(value, [95, 143, 187, 224]).clip(0, 4)
    out[:, :, :3][yellow] = YELLOW[ylevel[yellow]]
    out[:, :, :3][white & (value > 226)] = WHITE
    out[:, :, 3] = mask.astype('uint8') * 255
    if rim:
        # Dark contour outside, pink on exposed inner edges and chosen tips.
        outline = expanded(mask) & ~mask
        edge = mask & ~contracted(mask)
        yy, xx = np.indices(mask.shape)
        out[outline, :3] = INK
        out[outline, 3] = 255
        pink_edge = edge & (((xx * 5 + yy * 3 + phase * 7) % 11) < 4)
        out[pink_edge, :3] = PINK[(xx[pink_edge] + yy[pink_edge] + phase) % 2]
        pink_tips = outline & (((xx * 7 + yy * 5 + phase * 11) % 17) < 2)
        out[pink_tips, :3] = PINK[0]
        # Keep source glints inside the branching paths, away from the aura.
        glint = mask & ~edge & pink_source & (((xx + 2 * yy + phase) % 5) == 0)
        out[glint, :3] = PINK[2]
    return Image.fromarray(out, 'RGBA')


def flecks(cell: Image.Image, seed: int, count: int, *, h: int = 64, spread: int = 21) -> None:
    rng = np.random.default_rng(seed)
    d = ImageDraw.Draw(cell)
    for j in range(count):
        x = int(rng.integers(32 - spread, 33 + spread))
        y = int(rng.integers(8, h - 7))
        d.point((x, y), fill=tuple(PINK[2 if j % 4 == 0 else 0]) + (255,))


def put(native: Image.Image, art: Image.Image, col: int, row: int, y: int) -> None:
    native.alpha_composite(art, (col * S + (S - art.width) // 2, row * S + y))


def preview(atlas: Image.Image) -> None:
    # Four display pixels for each shipped pixel, including transparent cells.
    sheet = Image.new('RGBA', (3072, 3072), (11, 11, 26, 255))
    sheet.alpha_composite(atlas.resize((3072, 3072), N))
    contact = Image.new('RGB', (3072, 3472), '#0b0b1a')
    contact.paste(sheet.convert('RGB'))
    beam = atlas.crop((0, 0, 128, 384)).resize((48, 144), N)
    burst = atlas.crop((128, 512, 256, 640)).resize((68, 68), N)
    impact = atlas.crop((512, 512, 640, 640)).resize((68, 68), N)
    for i, bg in enumerate(('#101827', '#8b9bab')):
        tile = Image.new('RGBA', (1300, 270), bg)
        tile.alpha_composite(beam, (230, 52))
        tile.alpha_composite(burst, (650, 99))
        tile.alpha_composite(impact, (920, 99))
        contact.paste(tile.convert('RGB'), (168 + i * 1436, 3143))
    contact.save(OUT / 'preview.png')

    frames = []
    for i in range(6):
        panel = Image.new('RGBA', (480, 440), (11, 11, 26, 255))
        panel.alpha_composite(atlas.crop((i * 128, 0, (i + 1) * 128, 384)), (8, 8))
        for row, col, x, y, scale in ((3, i % 4, 154, 40, 84),
                                     (4, i % 4, 155, 165, 100),
                                     (3, 4 + i % 2, 328, 42, 80),
                                     (4, 4 + i % 2, 307, 169, 110),
                                     (5, 1 + i % 2, 317, 311, 86)):
            art = atlas.crop((col * 128, row * 128, (col + 1) * 128, (row + 1) * 128))
            panel.alpha_composite(art.resize((scale, scale), N), (x, y))
        frames.append(panel.convert('RGB'))
    frames[0].save(OUT / 'preview.gif', save_all=True, append_images=frames[1:],
                   duration=83, loop=0, optimize=False)


def build() -> None:
    bolts = Image.open(HERE / 'candidate_beams.png').convert('RGBA')
    fx = Image.open(HERE / 'candidate_effects.png').convert('RGBA')
    native = Image.new('RGBA', (384, 384))

    # Six independent painted bolts, with no scrolling or translated repeats.
    for i in range(6):
        x0, x1 = round(i * bolts.width / 6), round((i + 1) * bolts.width / 6)
        art = paint(bolts, (x0, 0, x1, bolts.height), (48, 178), i)
        cell = Image.new('RGBA', (64, 192))
        cell.alpha_composite(art, (8, 7))
        flecks(cell, 330 + i, 18, h=192, spread=24)
        put(native, cell, i, 0, 0)

    # Four separately painted charge balls grow from a common muzzle anchor.
    for i, size in enumerate(((14, 16), (22, 25), (30, 34), (40, 44))):
        box = (i * 384, 0, (i + 1) * 384, 390)
        art = paint(fx, box, size, 10 + i)
        cell = Image.new('RGBA', (64, 64))
        cell.alpha_composite(art, ((64 - size[0]) // 2, 57 - size[1]))
        flecks(cell, 450 + i, i + 2, spread=6 + i * 4)
        put(native, cell, i, 3, 0)

    # Dashes have an 8-pixel half-period, so both 64-pixel tiles wrap cleanly.
    for i in range(2):
        cell = Image.new('RGBA', (64, 64))
        d = ImageDraw.Draw(cell)
        for y in range(4 + i * 8, 64, 16):
            d.rectangle((30, y, 33, y + 4), fill=tuple(INK) + (255,))
            d.line((31, y, 31, y + 3), fill=tuple(WHITE) + (255,))
            d.line((32, y + 1, 32, y + 3), fill=tuple(YELLOW[3]) + (255,))
            d.point((30, y + 2), fill=tuple(PINK[0]) + (255,))
            d.point((33, y + 1), fill=tuple(PINK[1]) + (255,))
        put(native, cell, 4 + i, 3, 0)

    # Generated starbursts become ragged electric muzzle flare drawings.
    for i, size in enumerate(((52, 50), (49, 48), (50, 49), (51, 49))):
        art = paint(fx, (i * 384, 385, (i + 1) * 384, 733), size, 20 + i)
        cell = Image.new('RGBA', (64, 64))
        cell.alpha_composite(art, ((64 - size[0]) // 2, (64 - size[1]) // 2))
        flecks(cell, 550 + i, 4 + i, spread=23)
        put(native, cell, i, 4, 0)

    # The impact drawings are separate bottom-row forks, anchored to the rail.
    for i, size in enumerate(((50, 42), (48, 37), (43, 29))):
        box = (i * 512, 720, (i + 1) * 512, 1024)
        art = paint(fx, box, size, 30 + i)
        cell = Image.new('RGBA', (64, 64))
        cell.alpha_composite(art, ((64 - size[0]) // 2, 57 - size[1]))
        flecks(cell, 650 + i, 7 - i, spread=23 - i * 2)
        put(native, cell, 4 + i if i < 2 else 0, 4 if i < 2 else 5, 0)

    # Step the generated glow outward into a low-alpha static veil.
    for i in range(2):
        box = (i * 384, 385, (i + 1) * 384, 733)
        art = paint(fx, box, (49, 40), 40 + i, rim=False)
        a = np.array(art)
        yy, xx = np.indices(a.shape[:2])
        edge = np.minimum.reduce((xx, 48 - xx, yy, 39 - yy))
        taper = np.clip(edge / 7, 0, 1)
        core = a[:, :, 3] > 0
        shell1 = expanded(core)
        shell2 = expanded(shell1)
        shell3 = expanded(shell2)
        veil = np.where(core, 49, np.where(shell1, 34, np.where(shell2, 20,
                        np.where(shell3, 9, 0))))
        a[:, :, :3] = SLATE[5]
        a[:, :, 3] = (veil * taper).astype('uint8')
        put(native, Image.fromarray(a, 'RGBA'), 1 + i, 5, 12)

    atlas = native.resize((768, 768), N)
    atlas.save(OUT / 'storm_attack_minebeam.png')
    preview(atlas)


if __name__ == '__main__':
    build()
