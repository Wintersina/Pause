#!/usr/bin/env python3
"""Build the generated/painted Space flare sheets at their gameplay contract.

The source plates in candidates/ are image-generated painted pixel art.  This file
only does nearest-neighbour pixel cleanup, palette locking, phase animation, cell
composition, and seam-safe hand placement of additional arcs/sparks.
"""
from __future__ import annotations

import math
import random
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
CANDIDATES = Path(__file__).resolve().parent / "candidates"
N = 512
LEAD = 400
AMBIENT_CELL = 256

PALETTE = np.array(
    [
        (5, 6, 12),       # ink / deep indigo anchor
        (26, 16, 56),      # deep indigo shade
        (153, 87, 255),    # violet
        (255, 77, 200),    # hot magenta
        (255, 128, 40),    # orange
        (255, 200, 82),    # gold
        (255, 247, 220),   # white-hot
    ],
    dtype=np.float32,
)
HOT = (255, 247, 220, 255)
GOLD = (255, 200, 82, 255)
ORANGE = (255, 128, 40, 255)
MAGENTA = (255, 77, 200, 255)
VIOLET = (153, 87, 255, 255)
INDIGO = (26, 16, 56, 255)


def nearest_palette(im: Image.Image) -> Image.Image:
    """Lock painted RGB to the Space ramp without interpolating pixels."""
    a = np.array(im.convert("RGBA"), dtype=np.uint8)
    rgb = a[..., :3].astype(np.float32)
    # Weight luminance slightly so dark painted contouring survives the remap.
    d = ((rgb[..., None, :] - PALETTE[None, None, :, :]) ** 2).sum(axis=3)
    idx = d.argmin(axis=2)
    out = PALETTE[idx].astype(np.uint8)
    out[a[..., 3] == 0] = 0
    out = np.dstack((out, a[..., 3]))
    return Image.fromarray(out, "RGBA")


def nearest_resize(im: Image.Image, size: tuple[int, int]) -> Image.Image:
    return im.convert("RGBA").resize(size, Image.Resampling.NEAREST)


def translate_no_wrap(im: Image.Image, dx: int, dy: int = 0) -> Image.Image:
    src = np.array(im.convert("RGBA"), dtype=np.uint8)
    out = np.zeros_like(src)
    h, w = src.shape[:2]
    x0, x1 = max(0, dx), min(w, w + dx)
    sx0, sx1 = max(0, -dx), min(w, w - dx)
    y0, y1 = max(0, dy), min(h, h + dy)
    sy0, sy1 = max(0, -dy), min(h, h - dy)
    if x1 > x0 and y1 > y0:
        out[y0:y1, x0:x1] = src[sy0:sy1, sx0:sx1]
    return Image.fromarray(out, "RGBA")


def align_lead(im: Image.Image, target: int) -> Image.Image:
    a = np.array(im, dtype=np.uint8)
    col = (a[..., 3] > 190).sum(axis=0)
    # Use the strongest vertical painted edge, not a stray spark.
    current = int(np.argmax(col)) if col.max() else LEAD
    return translate_no_wrap(im, target - current)


def quantize_alpha(im: Image.Image, cap_tail: bool = True) -> Image.Image:
    a = np.array(im, dtype=np.uint8)
    alpha = a[..., 3].astype(np.int16)
    # Stepped airy transparency; the lead is allowed to remain hard/opaque.
    steps = np.array([0, 18, 30, 48, 72, 100, 132, 168, 205, 235, 255])
    alpha = steps[np.abs(alpha[..., None] - steps[None, :]).argmin(axis=2)]
    if cap_tail:
        alpha[:, :385] = np.minimum(alpha[:, :385], 145)
    a[..., 3] = alpha.astype(np.uint8)
    return Image.fromarray(a, "RGBA")


def seam(im: Image.Image) -> Image.Image:
    """Make the one-pixel vertical tile join exact after all drawing."""
    a = np.array(im.convert("RGBA"), dtype=np.uint8)
    a[-1] = a[0]
    return Image.fromarray(a, "RGBA")


def wrapped_line(layer: Image.Image, points, fill, width=1):
    """Draw a polyline in three vertical copies, then crop by the caller."""
    d = ImageDraw.Draw(layer)
    for off in (-N, 0, N):
        d.line([(int(x), int(y + off)) for x, y in points], fill=fill, width=width, joint="curve")


def add_painted_accents(base: Image.Image, frame: int, variant: int, warning: bool) -> Image.Image:
    """Add designed, hard-edged phase accents around the generated plate."""
    rng = random.Random(9000 + variant * 100 + frame * 17)
    phase = frame / 6.0
    lead = LEAD + round(8 * math.sin(2 * math.pi * (phase + .13)))
    layer = Image.new("RGBA", (N, N * 3), (0, 0, 0, 0))

    # Long looping magnetic arcs carry the motion through the whole seamless band.
    arc_count = 7 if warning else (13 if variant == 0 else 17)
    for i in range(arc_count):
        y0 = (i + .37) * N / arc_count + rng.uniform(-10, 10)
        length = rng.uniform(125, 285) if variant == 0 else rng.uniform(110, 230)
        amp = rng.uniform(9, 25) * (1.0 if variant == 0 else .72)
        freq = rng.choice((1.0, 1.5, 2.0, 2.5))
        curl = rng.choice((-1.0, 1.0))
        pts = []
        for s in range(0, int(length) + 2, 3):
            x = lead - s
            y = y0 + amp * math.sin(2 * math.pi * (freq * s / max(length, 1) + phase * curl + i * .11))
            pts.append((x, y % N))
        if warning:
            color = (255, 77, 200, 86 if i % 2 else 112)
            width = 1
        elif s < 70:
            color = (255, 200, 82, 176)
            width = 2
        else:
            color = ((255, 77, 200, 130) if i % 2 else (153, 87, 255, 112))
            width = 2 if i % 3 else 1
        wrapped_line(layer, pts, color, width)

    # Flame-lick hooks immediately behind the shock sheet.
    hooks = 3 if warning else (8 if variant == 0 else 11)
    for i in range(hooks):
        y0 = (i + .25) * N / hooks
        pts = []
        for s in range(0, 74 if variant == 0 else 58, 2):
            x = lead - s
            y = y0 + (10 + 5 * math.sin(i)) * math.sin(2 * math.pi * (s / 72 + phase + i * .15))
            pts.append((x, y % N))
        wrapped_line(layer, pts, (255, 128, 40, 130 if warning else 210), 2 if not warning else 1)

    # A few stepped heat ripples in the air ahead of the front.
    ripple_count = 2 if warning else 4
    for i in range(ripple_count):
        y0 = (i + .5) * N / ripple_count
        pts = []
        for s in range(0, 90, 4):
            x = lead + 7 + s
            y = y0 + 3 * math.sin(2 * math.pi * (s / 46 + phase + i * .4))
            pts.append((x, y % N))
        wrapped_line(layer, pts, (255, 247, 220, 48 if warning else 72), 1)

    # Core: the only deliberately opaque feature in a normal frame.
    pts = []
    for y in range(0, N + 1, 3):
        x = lead + 7 * math.sin(2 * math.pi * (y / N * 2 + phase))
        pts.append((x, y % N))
    if warning:
        wrapped_line(layer, pts, (255, 247, 220, 105), 1)
        wrapped_line(layer, [(x - 3, y) for x, y in pts], (255, 128, 40, 85), 1)
    else:
        wrapped_line(layer, [(x + 4, y) for x, y in pts], (255, 200, 82, 205), 4)
        wrapped_line(layer, pts, HOT, 3 if variant == 0 else 2)

    # Small sparks advance through a complete loop ahead of the shock.
    spark_count = 10 if warning else (38 if variant == 0 else 27)
    d = ImageDraw.Draw(layer)
    for i in range(spark_count):
        y = (i * 83 + frame * 29 + (i * i * 7)) % N
        travel = (i * 19 + frame * 31) % 96
        x = lead + 9 + travel
        r = 1 if i % 4 else 2
        alpha = 75 if warning else max(70, 210 - travel * 2)
        col = (255, 247, 220, alpha) if i % 3 else (255, 200, 82, alpha)
        for off in (-N, 0, N):
            if i % 2:
                d.rectangle((x - r, y + off - r, x + r, y + off + r), fill=col)
            else:
                d.point((x, y + off), fill=col)
                d.point((x - 1, y + off), fill=col)
                d.point((x, y + off - 1), fill=col)

    # Crop the central wrapped copy and composite without smoothing.
    accent = layer.crop((0, N, N, N * 2))
    return Image.alpha_composite(base, accent)


def flare_frame(source_name: str, frame: int, variant: int, warning=False) -> Image.Image:
    src = Image.open(CANDIDATES / source_name).convert("RGBA")
    src = nearest_resize(src, (N, N))
    src = nearest_palette(src)
    src = align_lead(src, LEAD + round(5 * math.sin(2 * math.pi * frame / 6 + variant)))
    a = np.array(src, dtype=np.uint8)
    # Keep image-generated tail airy while retaining hard paint at the shock.
    if warning:
        a[..., 3] = (a[..., 3].astype(np.float32) * .34).astype(np.uint8)
    src = Image.fromarray(a, "RGBA")
    src = add_painted_accents(src, frame, variant, warning)
    src = quantize_alpha(src, cap_tail=True)
    if warning:
        # Warning is intentionally a thin preview, never a filled bar.
        a = np.array(src, dtype=np.uint8)
        a[..., 3] = (a[..., 3].astype(np.float32) * .78).astype(np.uint8)
        src = Image.fromarray(a, "RGBA")
    return seam(src)


def trim_alpha(im: Image.Image) -> Image.Image:
    a = np.array(im.convert("RGBA"), dtype=np.uint8)
    ys, xs = np.where(a[..., 3] > 20)
    if len(xs) == 0:
        return im
    return im.crop((int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)))


def ambient_particle(kind: int, variant: int) -> Image.Image:
    src = Image.open(CANDIDATES / "ambient_painted.png").convert("RGBA")
    # The selected generated candidate is a clean 3x3 painterly particle board.
    boxes = {
        0: [(170, 210, 520, 520), (520, 210, 900, 520), (850, 190, 1190, 510), (200, 190, 560, 500)],
        1: [(170, 540, 540, 870), (520, 520, 900, 870), (850, 530, 1190, 870), (250, 520, 610, 860)],
        2: [(130, 820, 570, 1160), (500, 800, 930, 1160), (140, 820, 570, 1160), (500, 800, 930, 1160)],
        3: [(850, 820, 1150, 1090), (850, 820, 1150, 1090), (850, 820, 1150, 1090), (850, 820, 1150, 1090)],
    }
    piece = trim_alpha(src.crop(boxes[kind][variant]))
    piece = nearest_palette(piece)
    piece = piece.transpose([Image.Transpose.FLIP_LEFT_RIGHT, Image.Transpose.ROTATE_90, Image.Transpose.ROTATE_270, Image.Transpose.FLIP_TOP_BOTTOM][variant])
    target = {0: [78, 90, 64, 70], 1: [74, 90, 66, 82], 2: [104, 92, 78, 100], 3: [38, 46, 32, 42]}[kind][variant]
    scale = target / max(piece.size)
    size = (max(1, round(piece.width * scale)), max(1, round(piece.height * scale)))
    piece = nearest_resize(piece, size)
    cell = Image.new("RGBA", (AMBIENT_CELL, AMBIENT_CELL), (0, 0, 0, 0))
    cell.alpha_composite(piece, ((AMBIENT_CELL - piece.width) // 2, (AMBIENT_CELL - piece.height) // 2))
    return seam(cell)


def build_hazard():
    sheet = Image.new("RGBA", (N * 6, N * 3), (0, 0, 0, 0))
    for variant, source in enumerate(("flare_wide_painted.png", "flare_slim_painted.png")):
        for frame in range(6):
            sheet.alpha_composite(flare_frame(source, frame, variant), (frame * N, variant * N))
        for frame in range(2):
            sheet.alpha_composite(flare_frame(source, frame * 3 + 1, variant, warning=True), ((variant * 2 + frame) * N, N * 2))
    sheet.save(ROOT / "hazard.png")


def build_ambient():
    sheet = Image.new("RGBA", (AMBIENT_CELL * 8, AMBIENT_CELL * 2), (0, 0, 0, 0))
    for index in range(16):
        kind, variant = divmod(index, 4)
        cell = ambient_particle(kind, variant)
        x = (index % 8) * AMBIENT_CELL
        y = (index // 8) * AMBIENT_CELL
        sheet.alpha_composite(cell, (x, y))
    sheet.save(ROOT / "ambient.png")


if __name__ == "__main__":
    build_hazard()
    build_ambient()
    print("wrote", ROOT / "hazard.png")
    print("wrote", ROOT / "ambient.png")
