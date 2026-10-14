"""Build the Space cover-boulder destruction strip from intact and generated art."""
from pathlib import Path
import random
import shutil
from collections import deque

import numpy as np
from PIL import Image, ImageDraw


HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
CELL = 512
GEN = Path('/Users/sina/.codex/generated_images/01a137c2-3fe5-75f2-b325-32644a8b7559')
SOURCES = [
    'exec-fd197f2c-fb78-47fa-bd39-620bc8ab589c.png',
    'exec-be16f548-1360-444e-86a5-49c3a6ccea15.png',
    'exec-4235499a-2baf-459e-b8d0-e1ecd8315203.png',
]
for i, name in enumerate(SOURCES):
    dst = HERE / f'painted_burst_{i}.png'
    if not dst.exists():
        shutil.copyfile(GEN / name, dst)


def hard(a):
    a = a.copy()
    a[..., 3] = np.where(a[..., 3] >= 128, 255, 0).astype('uint8')
    a[a[..., 3] == 0, :3] = 0
    return a


def edge(mask):
    inside = mask.copy()
    for axis, direction in ((0, -1), (0, 1), (1, -1), (1, 1)):
        inside &= np.roll(mask, direction, axis=axis)
    return mask & ~inside


def neighbors(mask, steps=2):
    out = mask.copy()
    for _ in range(steps):
        out |= np.roll(out, 1, 0) | np.roll(out, -1, 0)
        out |= np.roll(out, 1, 1) | np.roll(out, -1, 1)
    return out


def seeded_regions(seeds):
    yy, xx = np.mgrid[:CELL, :CELL]
    distances = np.stack([(xx - x) ** 2 + (yy - y) ** 2 for x, y in seeds])
    return distances.argmin(0)


SEEDS = [
    [(125, 130), (255, 122), (381, 150), (139, 290), (284, 266), (368, 344), (239, 385)],
    [(136, 135), (258, 108), (377, 166), (127, 275), (265, 256), (383, 309), (266, 393)],
    [(142, 141), (276, 110), (384, 166), (147, 300), (278, 263), (365, 323), (257, 390)],
]


def brighten_existing_cracks(source, variant):
    out = source.copy()
    rgb = out[..., :3].astype('int16')
    pink = (rgb[..., 0] > 80) & (rgb[..., 2] > 75) & (rgb[..., 0] > rgb[..., 1] * 1.4)
    pink &= source[..., 3] > 128
    yy, xx = np.mgrid[:CELL, :CELL]
    sectors = [((xx < 325) & (yy > 150) & (yy < 350)),
               ((xx > 155) & (xx < 370) & (yy > 140) & (yy < 350)),
               ((xx > 165) & (yy > 100) & (yy < 365))]
    pink &= sectors[variant]
    rgb[pink, 0] = np.minimum(255, rgb[pink, 0] + 30)
    rgb[pink, 1] = np.minimum(255, rgb[pink, 1] + 16)
    rgb[pink, 2] = np.minimum(255, rgb[pink, 2] + 27)
    out[..., :3] = rgb.astype('uint8')
    return out


def crack_frame(source, variant):
    paths = [
        [[(151, 181), (184, 204), (209, 219), (237, 222), (268, 202), (295, 192)],
         [(259, 190), (270, 228), (283, 252), (296, 283), (323, 317), (358, 343)],
         [(173, 277), (205, 272), (234, 280), (261, 303), (297, 320), (337, 333)]],
        [[(168, 124), (190, 154), (207, 190), (233, 206), (246, 244), (250, 275), (273, 309), (282, 344)],
         [(298, 175), (316, 207), (310, 230), (343, 263), (375, 274), (401, 295)],
         [(155, 290), (190, 271), (222, 281), (250, 307), (277, 348)]],
        [[(207, 117), (212, 154), (240, 178), (266, 184), (281, 209)],
         [(167, 258), (201, 242), (229, 249), (253, 274), (274, 300), (284, 347)],
         [(318, 184), (344, 207), (351, 229), (386, 249)]],
    ]
    rng = random.Random(900 + variant)
    dark = Image.new('1', (CELL, CELL))
    glow = Image.new('1', (CELL, CELL))
    core = Image.new('1', (CELL, CELL))
    dd, gd, cd = ImageDraw.Draw(dark), ImageDraw.Draw(glow), ImageDraw.Draw(core)
    for poly in paths[variant]:
        points = []
        for (x0, y0), (x1, y1) in zip(poly[:-1], poly[1:]):
            segments = max(2, round(np.hypot(x1 - x0, y1 - y0) / 9))
            for step in range(segments):
                t = step / segments
                x = x0 + (x1 - x0) * t + rng.randint(-3, 3)
                y = y0 + (y1 - y0) * t + rng.randint(-3, 3)
                points.append((round(x), round(y)))
        points.append(poly[-1])
        dd.line(points, fill=1, width=7, joint='curve')
        gd.line(points, fill=1, width=3, joint='curve')
        for x, y in points[::3]:
            cd.point((x, y), fill=1)
    out = source.copy()
    op = source[..., 3] > 128
    d, g, c = (np.asarray(layer).astype(bool) & op for layer in (dark, glow, core))
    out[d, :3] = (36, 16, 49)
    out[g, :3] = (255, 85, 232)
    out[c, :3] = (255, 208, 246)
    # A few source-painted splinters lift just beyond the outer face.
    canvas = Image.fromarray(out)
    yy, xx = np.nonzero(op)
    for j, angle in enumerate((-2.45, -1.12, 1.96)):
        ux, uy = np.cos(angle), np.sin(angle)
        k = np.argmax((xx - 256) * ux + (yy - 256) * uy)
        x, y = int(xx[k]), int(yy[k])
        size = 14 + j * 2
        chip = source_chip(source, round(x - ux * 5), round(y - uy * 5), size, rng)
        px = round(x + ux * 8 - size / 2)
        py = round(y + uy * 8 - size / 2)
        canvas.alpha_composite(chip, (min(510 - size, max(2, px)),
                                      min(510 - size, max(2, py))))
    return hard(np.asarray(canvas))


def painted_burst(variant):
    image = Image.open(HERE / f'painted_burst_{variant}.png').convert('RGBA')
    a = np.asarray(image)
    ys, xs = np.nonzero(a[..., 3] >= 160)
    # Keep the original candidate's painted fragments, including its small chips.
    image = image.crop((max(0, xs.min() - 3), max(0, ys.min() - 3),
                        min(image.width, xs.max() + 4), min(image.height, ys.max() + 4)))
    scale = 478 / max(image.size)
    image = image.resize((round(image.width * scale), round(image.height * scale)), Image.Resampling.NEAREST)
    out = Image.new('RGBA', (CELL, CELL))
    out.alpha_composite(image, ((CELL - image.width) // 2, (CELL - image.height) // 2))
    return hard(np.asarray(out))


def source_chip(source, x, y, size, rng):
    """A tiny irregular shard retaining the intact rock's own painted pixels."""
    a = np.zeros((size, size, 4), dtype='uint8')
    sy, sx = y - size // 2, x - size // 2
    crop = source[max(0, sy):min(CELL, sy + size), max(0, sx):min(CELL, sx + size)]
    a[:crop.shape[0], :crop.shape[1]] = crop
    mask = Image.new('1', (size, size))
    dr = ImageDraw.Draw(mask)
    points = [(round(size * (.5 + .45 * np.cos(t) * rng.uniform(.65, 1.0))),
               round(size * (.5 + .45 * np.sin(t) * rng.uniform(.65, 1.0))))
              for t in np.linspace(0, 2 * np.pi, rng.randint(5, 8), endpoint=False)]
    dr.polygon(points, fill=1)
    a[..., 3] = np.where(np.asarray(mask), a[..., 3], 0)
    a[a[..., 3] == 0, :3] = 0
    return Image.fromarray(a)


def scatter_chips(canvas, source, variant, count, spread, sizes, seed):
    rng = random.Random(seed)
    mask = source[..., 3] > 128
    ys, xs = np.nonzero(mask)
    for j in range(count):
        k = rng.randrange(len(xs))
        size = rng.randint(*sizes)
        chip = source_chip(source, int(xs[k]), int(ys[k]), size, rng)
        angle = 2 * np.pi * (j / count + rng.uniform(-.09, .09))
        radius = rng.uniform(spread[0], spread[1])
        px = round(256 + radius * np.cos(angle) - size / 2)
        py = round(256 + radius * np.sin(angle) - size / 2)
        px = min(508 - size, max(4, px))
        py = min(508 - size, max(4, py))
        canvas.alpha_composite(chip, (px, py))
    dr = ImageDraw.Draw(canvas)
    for _ in range(count * 2):
        x, y = rng.randrange(15, 497), rng.randrange(15, 497)
        if rng.random() < .58:
            color = rng.choice(((255, 85, 232, 255), (255, 208, 246, 255), (122, 220, 250, 255)))
        else:
            color = rng.choice(((50, 60, 89, 255), (95, 102, 132, 255)))
        s = rng.choice((1, 1, 2, 2, 3))
        dr.rectangle((x, y, x + s, y + s), fill=color)


def late_breakup(burst, source, variant):
    canvas = Image.new('RGBA', (CELL, CELL))
    occupied = burst[..., 3] > 128
    seen = np.zeros_like(occupied)
    for sy, sx in zip(*np.nonzero(occupied)):
        if seen[sy, sx]:
            continue
        queue = deque([(int(sy), int(sx))])
        seen[sy, sx] = True
        pixels = []
        while queue:
            y, x = queue.popleft()
            pixels.append((y, x))
            for ny, nx in ((y-1, x), (y+1, x), (y, x-1), (y, x+1)):
                if 0 <= ny < CELL and 0 <= nx < CELL and occupied[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True
                    queue.append((ny, nx))
        if len(pixels) < 5:
            continue
        ys, xs = np.array(pixels).T
        x0, x1, y0, y1 = xs.min(), xs.max()+1, ys.min(), ys.max()+1
        piece = np.zeros((y1-y0, x1-x0, 4), dtype='uint8')
        piece[ys-y0, xs-x0] = burst[ys, xs]
        image = Image.fromarray(piece)
        scale = .83 if len(pixels) > 2000 else .77
        image = image.resize((max(1, round(image.width * scale)),
                              max(1, round(image.height * scale))), Image.Resampling.NEAREST)
        cx, cy = (x0+x1)/2, (y0+y1)/2
        x = round(cx + (cx-256)*.17 - image.width/2)
        y = round(cy + (cy-256)*.17 - image.height/2)
        x = min(510-image.width, max(2, x))
        y = min(510-image.height, max(2, y))
        canvas.alpha_composite(image, (x, y))
    scatter_chips(canvas, source, variant, 20, (120, 220), (5, 16), 2200 + variant)
    return hard(np.asarray(canvas))


def last_debris(source, variant):
    canvas = Image.new('RGBA', (CELL, CELL))
    scatter_chips(canvas, source, variant, 44, (70, 220), (16, 35), 4200 + variant)
    return hard(np.asarray(canvas))


def remove_reference_red(frame):
    """Two stray crimson pixels in the supplied third asteroid stay magenta here."""
    out = frame.copy()
    rgb = out[..., :3]
    dark = np.all(rgb == (193, 8, 53), axis=2) & (out[..., 3] > 128)
    light = np.all(rgb == (254, 114, 147), axis=2) & (out[..., 3] > 128)
    out[dark, :3] = (193, 8, 101)
    out[light, :3] = (254, 114, 235)
    return out


def main():
    intact = np.asarray(Image.open(ROOT / 'boulder.png').convert('RGBA'))
    sheet = Image.new('RGBA', (CELL * 5, CELL * 3))
    for v in range(3):
        src = intact[:, v * CELL:(v + 1) * CELL].copy()
        burst = painted_burst(v)
        frames = [remove_reference_red(frame) for frame in
                  (brighten_existing_cracks(src, v), crack_frame(src, v), burst,
                   late_breakup(burst, src, v), last_debris(src, v))]
        for f, frame in enumerate(frames):
            sheet.paste(Image.fromarray(frame), (f * CELL, v * CELL))
        areas = [(frame[..., 3] > 128).sum() for frame in frames]
        print('variant', v, 'relative areas', [round(n / areas[0], 3) for n in areas])
    sheet.save(ROOT / 'break.png')


if __name__ == '__main__':
    main()
