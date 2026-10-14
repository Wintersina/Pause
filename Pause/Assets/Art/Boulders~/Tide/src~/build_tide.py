"""Compose Tide cover art from selected image-generated paintings.

The rock faces in every break frame are pixels cut from the corresponding
intact painting. Only seams, motion, and small sea debris are drawn here.
"""
from pathlib import Path
import math
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter


ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
CELL = 768
SELECTED = (0, 6, 5)
WIDTHS = (634, 676, 674)
RNG = random.Random(24014)


def prepare(index, width):
    source = Image.open(SRC / f"candidate_{index}.png").convert("RGBA")
    ar = np.asarray(source)
    yy, xx = np.where(ar[:, :, 3] > 12)
    box = (int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1)
    source = source.crop(box)
    height = round(source.height * width / source.width)
    source = source.resize((width, height), Image.Resampling.LANCZOS)
    a = np.array(source, dtype=np.float32)
    rgb = a[:, :, :3]
    l = rgb @ np.array([.299, .587, .114], dtype=np.float32)
    # Compress the brighter generated painting toward the backdrop's dark
    # landmark ramp, keeping local hue and all source texture variation.
    out = np.stack((.46 * rgb[:, :, 0] + .16 * l,
                    .55 * rgb[:, :, 1] + .22 * l,
                    .53 * rgb[:, :, 2] + .18 * l), axis=2)
    out = np.minimum(out, 177)
    # Clean accidental red/pink colour casts from shell flecks.
    out[:, :, 0] = np.minimum(out[:, :, 0], np.maximum(out[:, :, 1] * .90, l * .48))
    # The generated alpha provides the silhouette; preserve fully solid faces.
    a[:, :, :3] = out
    a[:, :, 3] = np.where(a[:, :, 3] >= 238, 255, a[:, :, 3])
    a[:, :, 3] = np.where(a[:, :, 3] < 14, 0, a[:, :, 3])
    source = Image.fromarray(np.clip(a, 0, 255).astype("uint8"), "RGBA")
    cell = Image.new("RGBA", (CELL, CELL))
    pos = ((CELL - width) // 2, (CELL - height) // 2 - 12)
    cell.alpha_composite(source, pos)
    return add_skirt(cell)


def add_skirt(im):
    """Thin source-like foam into an 80 px lower alpha ramp."""
    a = np.asarray(im)
    yy, xx = np.where(a[:, :, 3] > 128)
    bottom = int(yy.max())
    left, right = int(xx.min()), int(xx.max())
    skirt = Image.new("RGBA", (CELL, CELL))
    d = ImageDraw.Draw(skirt, "RGBA")
    for _ in range(570):
        x = RNG.randrange(max(12, left - 14), min(CELL - 12, right + 14))
        t = RNG.random()
        y = int(bottom - 23 + t * 78 + RNG.uniform(-15, 15))
        if y < 12 or y > CELL - 14:
            continue
        p = (1 - t) ** 1.45
        if RNG.random() > p:
            continue
        r = RNG.choice((1, 1, 2, 2, 3, 4))
        color = RNG.choice(((27, 98, 80), (37, 118, 91), (55, 137, 107), (15, 56, 50)))
        alpha = int(10 + 80 * p)
        d.ellipse((x - r, y - r // 2, x + r, y + r // 2), fill=(*color, alpha))
    # Underlay first, so the original opaque body keeps its exact painting.
    skirt.alpha_composite(im)
    return skirt


def seam_paths(v):
    if v == 0:
        return [[(353, 162), (364, 235), (341, 301), (374, 374), (350, 447), (378, 563)],
                [(240, 313), (296, 350), (341, 301)],
                [(374, 374), (457, 406), (503, 470)]]
    if v == 1:
        return [[(315, 259), (346, 313), (326, 370), (354, 415), (340, 540)],
                [(346, 313), (425, 323), (478, 368)],
                [(219, 406), (282, 426), (326, 370)]]
    return [[(343, 219), (375, 291), (358, 354), (390, 423), (365, 521)],
            [(201, 346), (284, 366), (358, 354)],
            [(390, 423), (469, 384), (552, 422)]]


def cracks(base, v, stage):
    solid = np.asarray(base)[:, :, 3] > 245
    glow = Image.new("RGBA", (CELL, CELL))
    gd = ImageDraw.Draw(glow, "RGBA")
    for j, path in enumerate(seam_paths(v)):
        if stage == 0 and j > 0:
            continue
        if stage == 0:
            path = path[1:4]
        gd.line(path, fill=(78, 179, 151, 105 if stage == 0 else 175), width=8 if stage else 5, joint="curve")
        gd.line(path, fill=(156, 255, 224, 205 if stage == 0 else 255), width=3 if stage else 2, joint="curve")
        if stage > 0:
            gd.line(path, fill=(239, 255, 250, 220), width=1, joint="curve")
    g = np.array(glow)
    g[:, :, 3] = np.where(solid, g[:, :, 3], 0)
    result = base.copy()
    result.alpha_composite(Image.fromarray(g))
    return result


def regions(v):
    # Nine irregular Voronoi slabs. Their pixels all come from the intact art.
    if v == 0:
        sites = [(300, 190), (421, 195), (268, 326), (389, 326), (500, 333),
                 (255, 456), (372, 459), (490, 467), (377, 575)]
    elif v == 1:
        sites = [(210, 300), (347, 283), (502, 293), (229, 409), (384, 394),
                 (544, 407), (257, 517), (397, 515), (525, 524)]
    else:
        sites = [(243, 284), (386, 255), (521, 292), (221, 397), (378, 384),
                 (552, 403), (274, 520), (408, 527), (516, 514)]
    y, x = np.mgrid[0:CELL, 0:CELL]
    wave = 9 * np.sin(x * .071 + y * .041) + 5 * np.sin(y * .11 - x * .039)
    dist = [((x - sx + wave) ** 2 + (y - sy - wave) ** 2) for sx, sy in sites]
    return np.argmin(np.stack(dist), axis=0).astype(np.uint8), sites


def draw_debris(im, n, extent, fade):
    d = ImageDraw.Draw(im, "RGBA")
    for _ in range(n):
        ang = RNG.random() * 2 * math.pi
        rad = RNG.uniform(55, extent)
        x = int(384 + math.cos(ang) * rad)
        y = int(390 + math.sin(ang) * rad)
        if not (8 < x < 760 and 8 < y < 760):
            continue
        r = RNG.choice((2, 3, 4, 5, 7, 9))
        if RNG.random() < .55:
            d.ellipse((x-r, y-r, x+r, y+r), outline=(83, 170, 142, fade), width=1)
        else:
            d.polygon(((x-r, y), (x, y-r//2), (x+r, y+1), (x, y+r//2)),
                      fill=(38, 88, 73, fade))


def broken(base, v, frame):
    label, sites = regions(v)
    src = np.asarray(base)
    canvas = Image.new("RGBA", (CELL, CELL))
    if frame == 2:
        # The flash is exposed only in the widening fissures under the chunks.
        canvas = cracks(base, v, 1)
        ca = np.array(canvas)
        ca[:, :, 3] = (ca[:, :, 3] * .22).astype(np.uint8)
        canvas = Image.fromarray(ca)
    scale = (1., .98, .91, .74, .34)[frame]
    drift = (0, 6, 33, 77, 142)[frame]
    for i, (sx, sy) in enumerate(sites):
        m = (label == i) & (src[:, :, 3] > 0)
        part = np.array(src)
        part[:, :, 3] = np.where(m, part[:, :, 3], 0)
        piece = Image.fromarray(part)
        # Remove a thin jagged cut line between slabs during the burst.
        if frame >= 2:
            pa = piece.getchannel("A").filter(ImageFilter.MinFilter(5))
            piece.putalpha(pa)
        bounds = piece.getbbox()
        if not bounds:
            continue
        piece = piece.crop(bounds)
        if scale < 1:
            piece = piece.resize((max(1, round(piece.width * scale)),
                                  max(1, round(piece.height * scale))), Image.Resampling.LANCZOS)
        cx = (bounds[0] + bounds[2]) / 2
        cy = (bounds[1] + bounds[3]) / 2
        dx, dy = sx - 384, sy - 384
        length = max(1, math.hypot(dx, dy))
        ox = drift * dx / length + RNG.uniform(-3, 3)
        oy = drift * dy / length + RNG.uniform(-3, 3)
        px = round(cx + ox - piece.width / 2)
        py = round(cy + oy - piece.height / 2)
        canvas.alpha_composite(piece, (px, py))
    if frame == 1:
        canvas = cracks(canvas, v, 1)
    if frame == 2:
        canvas = cracks(canvas, v, 1)
        # Mint-white seams appear in the space between the painting's own
        # displaced slabs, strongest at the instant they separate.
        ca = np.array(canvas)
        core = (label != np.roll(label, 1, axis=0)) | (label != np.roll(label, 1, axis=1))
        wide = Image.fromarray((core * 255).astype("uint8")).filter(ImageFilter.MaxFilter(11))
        narrow = Image.fromarray((core * 255).astype("uint8")).filter(ImageFilter.MaxFilter(3))
        exposed = (src[:, :, 3] > 128) & (ca[:, :, 3] < 190)
        light = np.zeros((CELL, CELL, 4), dtype=np.uint8)
        wm = (np.asarray(wide) > 0) & exposed
        nm = (np.asarray(narrow) > 0) & exposed
        light[wm] = (104, 224, 186, 135)
        light[nm] = (239, 255, 250, 195)
        canvas.alpha_composite(Image.fromarray(light))
    if frame >= 2:
        draw_debris(canvas, (70, 110, 165)[frame-2], (280, 330, 345)[frame-2], (180, 125, 65)[frame-2])
    # Hard guard against any shifted coral fleck reaching a neighbouring cell.
    clipped = np.array(canvas)
    clipped[:5, :, 3] = 0
    clipped[-5:, :, 3] = 0
    clipped[:, :5, 3] = 0
    clipped[:, -5:, 3] = 0
    canvas = Image.fromarray(clipped)
    return canvas


def main():
    boulder = Image.new("RGBA", (CELL * 3, CELL))
    strip = Image.new("RGBA", (CELL * 5, CELL * 3))
    for v, (idx, width) in enumerate(zip(SELECTED, WIDTHS)):
        base = prepare(idx, width)
        boulder.alpha_composite(base, (v * CELL, 0))
        strip.alpha_composite(cracks(base, v, 0), (0, v * CELL))
        for frame in range(1, 5):
            strip.alpha_composite(broken(base, v, frame), (frame * CELL, v * CELL))
    boulder.save(ROOT / "boulder.png")
    strip.save(ROOT / "break.png")


if __name__ == "__main__":
    main()
