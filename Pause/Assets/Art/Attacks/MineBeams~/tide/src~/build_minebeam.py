"""Build Tide's painted water-jet atlas at native 64 px cells.

The three candidate PNGs are generated source paintings. Cropping, cleanup,
palette control, and composition are deterministic; export is one x2 NEAREST.
"""
from __future__ import annotations

import colorsys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
OUT = HERE.parent
S = 64
NEAREST = Image.Resampling.NEAREST

INK = (4, 18, 28, 255)
WATER = [
    (4, 18, 28),       # ocean ink
    (15, 43, 67),      # deep blue undercurrent
    (28, 83, 116),     # pressure shadow
    (57, 132, 172),    # aqua-blue body (hue 202)
    (103, 174, 207),   # lifted water
    (170, 216, 233),   # foam shade
    (232, 247, 250),   # white-water
]
PINK = [
    (4, 18, 28),
    (74, 30, 73),
    (139, 37, 120),
    (210, 54, 169),
    (255, 79, 216),
    (255, 138, 230),
    (255, 224, 248),
]


def alpha_bounds(im: Image.Image, threshold: int = 128) -> tuple[int, int, int, int]:
    a = np.asarray(im.convert("RGBA"))[:, :, 3]
    yy, xx = np.where(a >= threshold)
    return (int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1)


def fitted(source: Image.Image, box: tuple[int, int, int, int], size: tuple[int, int]) -> Image.Image:
    crop = source.crop(box)
    crop = crop.crop(alpha_bounds(crop))
    return crop.resize(size, NEAREST)


def clean(source: Image.Image, *, rim: bool = True, opacity: float = 1.0) -> Image.Image:
    """Retain the painting's texture while snapping hue/value to safe ramps."""
    a = np.asarray(source.convert("RGBA"), dtype=np.uint8)
    h, w = a.shape[:2]
    out = np.zeros((h, w, 4), dtype=np.uint8)
    solid = a[:, :, 3] >= 145
    yy, xx = np.where(solid)
    for y, x in zip(yy, xx):
        r, g, b = (int(v) for v in a[y, x, :3])
        hue, sat, val = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
        hue *= 360
        level = 0 if val < .12 else 1 if val < .23 else 2 if val < .38 else 3 if val < .55 else 4 if val < .73 else 5 if val < .91 else 6
        if level == 6 and not (val > .98 and sat < .16):
            level = 5
        # Pink in the paintings is part of their sparkle; remap blue-violet
        # shadows to ocean water so the material still reads as a water jet.
        painted_pink = 290 <= hue <= 333 and sat > .30 and val > .51
        if painted_pink:
            level = max(2, level)
        out[y, x, :3] = (PINK if painted_pink else WATER)[level]
        out[y, x, 3] = min(255, round(int(a[y, x, 3]) * opacity))

    if rim:
        # Native pixel boundary, following every painted spray spur. Keep the
        # deep ink one pixel outside it for legibility on pale surf tiles.
        mask = out[:, :, 3] >= 128
        inner = mask.copy()
        inner[1:, :] &= mask[:-1, :]
        inner[:-1, :] &= mask[1:, :]
        inner[:, 1:] &= mask[:, :-1]
        inner[:, :-1] &= mask[:, 1:]
        edge = mask & ~inner
        for y, x in zip(*np.where(edge)):
            out[y, x, :3] = PINK[5 if (x + 2 * y) % 5 == 0 else 4]
        # Four-neighbour ink outline, excluding pixels already in the art.
        outside = np.zeros_like(mask)
        outside[1:, :] |= mask[:-1, :]
        outside[:-1, :] |= mask[1:, :]
        outside[:, 1:] |= mask[:, :-1]
        outside[:, :-1] |= mask[:, 1:]
        outside &= ~mask
        out[outside] = INK
    return Image.fromarray(out, "RGBA")


def put(cell: Image.Image, art: Image.Image, x: int, y: int) -> None:
    cell.alpha_composite(art, (x, y))


def particle(gl: ImageDraw.ImageDraw, x: int, y: int, phase: int) -> None:
    gl.point((x, y), fill=INK)
    gl.point((x + 1, y), fill=(*PINK[4], 230))
    if phase % 3 == 0:
        gl.point((x + 1, y - 1), fill=(*PINK[6], 255))


def beam_cell(jets: Image.Image, frame: int) -> Image.Image:
    # Each frame is its own generated painting. Trim the top spray cap and
    # brass nozzle to leave continuous pressure flow across the lane.
    left = frame * 256
    source = jets.crop((left + 5, 162, left + 251, 920))
    art = clean(source.resize((43, 184), NEAREST))
    cell = Image.new("RGBA", (S, S * 3))
    put(cell, art, 10, 4)
    dr = ImageDraw.Draw(cell)
    # Fine cast-off spray uses fixed phases tied to each distinct painting.
    rng = np.random.default_rng(730 + frame)
    for i in range(22):
        y = int(rng.integers(9, 183))
        side = -1 if (i + frame) % 2 == 0 else 1
        x = int(32 + side * rng.integers(21, 24))
        particle(dr, x, y, i + frame)
    return cell


def pilot_cell(pilots: Image.Image, frame: int) -> Image.Image:
    spans = [(0, 350), (350, 850), (850, 1450), (1450, 2172)]
    sizes = [(13, 20), (21, 30), (31, 42), (40, 50)]
    lo, hi = spans[frame]
    w, h = sizes[frame]
    art = clean(fitted(pilots, (lo, 0, hi, pilots.height), (w, h)))
    cell = Image.new("RGBA", (S, S))
    put(cell, art, (S - w) // 2, 58 - h)
    return cell


def aim_cell(frame: int) -> Image.Image:
    cell = Image.new("RGBA", (S, S))
    dr = ImageDraw.Draw(cell)
    # Eight-pixel droplet rhythm tiles seamlessly along the aim line.
    for y in range(-8, 72, 8):
        yy = y + frame * 4
        for py in range(yy, yy + 3):
            if not 0 <= py < S:
                continue
            dr.point((29, py), fill=INK)
            dr.point((30, py), fill=(*PINK[4], 235))
            dr.point((31, py), fill=(*WATER[5], 240))
            dr.point((32, py), fill=(*PINK[5], 235))
            dr.point((33, py), fill=INK)
        if 0 <= yy + 4 < S:
            dr.point((31, yy + 4), fill=(*PINK[6], 225))
    return cell


def burst_cell(splashes: Image.Image, frame: int) -> Image.Image:
    spans = [(0, 220), (220, 480), (480, 780), (780, 1110)]
    sizes = [(32, 37), (39, 43), (47, 49), (53, 52)]
    lo, hi = spans[frame]
    w, h = sizes[frame]
    art = clean(fitted(splashes, (lo, 0, hi, splashes.height), (w, h)))
    cell = Image.new("RGBA", (S, S))
    put(cell, art, (S - w) // 2, 58 - h)
    return cell


def impact_cell(splashes: Image.Image, frame: int) -> Image.Image:
    spans = [(1100, 1470), (1470, 1810), (1810, 2172)]
    sizes = [(54, 43), (50, 34), (43, 27)]
    lo, hi = spans[frame]
    w, h = sizes[frame]
    art = clean(fitted(splashes, (lo, 0, hi, splashes.height), (w, h)), opacity=[1, .84, .62][frame])
    cell = Image.new("RGBA", (S, S))
    put(cell, art, (S - w) // 2, 58 - h)
    return cell


def haze_cell(splashes: Image.Image, frame: int) -> Image.Image:
    lo, hi = (1100, 1470) if frame == 0 else (1470, 1810)
    art = clean(fitted(splashes, (lo, 0, hi, splashes.height), (45 + 3 * frame, 42)), rim=False)
    data = np.array(art)
    yy, xx = np.mgrid[:art.height, :art.width]
    edge = np.minimum.reduce((xx, art.width - 1 - xx, yy, art.height - 1 - yy))
    fade = np.minimum(1, edge / 6)
    data[:, :, 3] = (np.minimum(data[:, :, 3], 53) * fade).astype("uint8")
    # Keep a pale ocean-white mist, with the source painting's wisps intact.
    data[:, :, :3] = np.where(data[:, :, :3] > 120, 170, data[:, :, :3])
    cell = Image.new("RGBA", (S, S))
    put(cell, Image.fromarray(data, "RGBA"), (S - art.width) // 2, 11)
    return cell


def build() -> None:
    jets = Image.open(HERE / "candidate_jets.png").convert("RGBA")
    splashes = Image.open(HERE / "candidate_splashes.png").convert("RGBA")
    pilots = Image.open(HERE / "candidate_pilots.png").convert("RGBA")
    native = Image.new("RGBA", (6 * S, 6 * S))
    for i in range(6):
        put(native, beam_cell(jets, i), i * S, 0)
    for i in range(4):
        put(native, pilot_cell(pilots, i), i * S, 3 * S)
    for i in range(2):
        put(native, aim_cell(i), (4 + i) * S, 3 * S)
    for i in range(4):
        put(native, burst_cell(splashes, i), i * S, 4 * S)
    for i in range(2):
        put(native, impact_cell(splashes, i), (4 + i) * S, 4 * S)
    put(native, impact_cell(splashes, 2), 0, 5 * S)
    for i in range(2):
        put(native, haze_cell(splashes, i), (1 + i) * S, 5 * S)
    atlas = native.resize((768, 768), NEAREST)
    atlas.save(OUT / "tide_attack_minebeam.png")
    preview(atlas, native)


def preview(atlas: Image.Image, native: Image.Image) -> None:
    # 4x native nearest contact sheet: every atlas cell shown in place.
    sheet = native.resize((1536, 1536), NEAREST)
    result = Image.new("RGB", (1536, 1780), "#0b0b1a")
    result.paste(sheet, (0, 0), sheet)
    body = atlas.crop((0, 0, 128, 384)).resize((42, 126), NEAREST)
    burst = atlas.crop((3 * 128, 512, 4 * 128, 640)).resize((64, 64), NEAREST)
    impact = atlas.crop((4 * 128, 512, 5 * 128, 640)).resize((55, 55), NEAREST)
    for i, bg in enumerate(("#0b0b1a", "#a8dbe8")):
        tile = Image.new("RGB", (580, 190), bg)
        tile.paste(body, (110, 30), body)
        tile.paste(burst, (288, 74), burst)
        tile.paste(impact, (415, 70), impact)
        result.paste(tile, (120 + i * 720, 1564))
    result.save(OUT / "preview.png")

    frames = []
    for i in range(6):
        panel = Image.new("RGBA", (400, 416), (11, 11, 26, 255))
        panel.alpha_composite(atlas.crop((128 * i, 0, 128 * (i + 1), 384)), (4, 4))
        p = atlas.crop((128 * (i % 4), 384, 128 * (i % 4 + 1), 512))
        b = atlas.crop((128 * (i % 4), 512, 128 * (i % 4 + 1), 640))
        aim = atlas.crop((128 * (4 + i % 2), 384, 128 * (5 + i % 2), 512))
        impact_x, impact_y = ((4 + i % 2) * 128, 512) if i % 3 < 2 else (0, 640)
        imp = atlas.crop((impact_x, impact_y, impact_x + 128, impact_y + 128))
        for art, pos in ((p, (144, 132)), (b, (232, 130)), (aim, (320, 40)), (imp, (320, 206))):
            panel.alpha_composite(art.resize((72, 72), NEAREST), pos)
        frames.append(panel.convert("RGB"))
    frames[0].save(OUT / "preview.gif", save_all=True, append_images=frames[1:], duration=83, loop=0, optimize=False)


if __name__ == "__main__":
    build()
