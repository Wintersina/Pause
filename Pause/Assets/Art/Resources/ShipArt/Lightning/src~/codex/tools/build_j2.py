#!/usr/bin/env python3
"""Build the J2 Lightning flame sheets from an image-generated painted source.

The source is deliberately reduced to the exact J2 key palette before it is
resized and arranged. All final pixels are opaque RGB: green is the key color.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
W, H, FRAMES = 256, 1024, 7
GREEN = (0, 255, 0)
PALETTE = [
    (142, 132, 20),  # dark olive
    (238, 220, 50),  # lightning yellow
    (110, 242, 238),  # cyan
    (244, 234, 212),  # warm bone-white
    (242, 134, 43),  # orange
    GREEN,
]
OLIVE, YELLOW, CYAN, WHITE, ORANGE, _ = PALETTE


def nearest(rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    return min(PALETTE, key=lambda c: sum((a - b) ** 2 for a, b in zip(rgb, c)))


def quantize(im: Image.Image) -> Image.Image:
    src = im.convert("RGB")
    out = Image.new("RGB", src.size, GREEN)
    spx, opx = src.load(), out.load()
    for y in range(src.height):
        for x in range(src.width):
            opx[x, y] = nearest(spx[x, y])
    return out


def content_bbox(im: Image.Image) -> tuple[int, int, int, int]:
    px = im.load()
    xs: list[int] = []
    ys: list[int] = []
    for y in range(im.height):
        for x in range(im.width):
            if px[x, y] != GREEN:
                xs.append(x)
                ys.append(y)
    if not xs:
        raise ValueError("generated source contains no non-green painted content")
    return min(xs), min(ys), max(xs) + 1, max(ys) + 1


def row_warp(im: Image.Image, phase: float, amplitude: int) -> Image.Image:
    """Waver a plume by whole-pixel row offsets, keeping nearest-neighbor edges."""
    out = Image.new("RGB", im.size, GREEN)
    src, dst = im.load(), out.load()
    for y in range(im.height):
        t = y / max(1, im.height - 1)
        shift = round(amplitude * (math.sin(t * math.tau + phase) * 0.68 + math.sin(t * math.tau * 2.1 + phase * 0.7) * 0.32))
        for x in range(im.width):
            sx = x - shift
            if 0 <= sx < im.width:
                dst[x, y] = src[sx, y]
    return out


def recolor_hot(im: Image.Image, boost: bool = False, smear: bool = False) -> Image.Image:
    """Make boost brighter and frame 2 a mostly bone-white speed streak."""
    out = im.copy()
    px = out.load()
    cx = im.width / 2
    for y in range(im.height):
        for x in range(im.width):
            c = px[x, y]
            if c == GREEN:
                continue
            rel = abs(x - cx) / max(1.0, cx)
            if smear:
                # Preserve a few cyan/orange energy traces, but make the streak
                # itself near-solid hot white.
                if c == CYAN and (x + y) % 13 == 0:
                    px[x, y] = CYAN
                elif c == ORANGE and (x * 3 + y) % 29 == 0:
                    px[x, y] = ORANGE
                elif rel > 0.82 and c == YELLOW:
                    px[x, y] = YELLOW
                else:
                    px[x, y] = WHITE
            elif boost:
                # Widen the white-hot core in the central body while retaining
                # the generated source's cyan bolt and warm edge accents.
                if rel < 0.38 and c in (YELLOW, CYAN):
                    px[x, y] = WHITE
                elif c == OLIVE and rel < 0.62 and y < im.height * 0.82:
                    px[x, y] = YELLOW
    return out


def add_sparks(cell: Image.Image, frame: int, boost: bool) -> None:
    """Add sparse detached, keyed spark chips to reinforce the old identity."""
    px = cell.load()
    # Positions are normalized to each cell and intentionally stay well inside
    # the 8% side safety margins used by check_j2.py.
    base = [
        (0.25, 0.22, CYAN, 2), (0.72, 0.28, ORANGE, 2),
        (0.19, 0.43, YELLOW, 1), (0.79, 0.47, CYAN, 2),
        (0.30, 0.62, ORANGE, 1), (0.70, 0.69, CYAN, 1),
        (0.23, 0.80, CYAN, 1), (0.76, 0.84, ORANGE, 1),
    ]
    if boost:
        base += [
            (0.14, 0.34, CYAN, 1), (0.86, 0.38, CYAN, 2),
            (0.16, 0.57, ORANGE, 1), (0.84, 0.64, YELLOW, 1),
            (0.35, 0.90, CYAN, 1), (0.65, 0.93, CYAN, 1),
        ]
    for i, (rx, ry, color, size) in enumerate(base):
        # Frame-specific phase makes the detached chips flicker without making
        # a static rectangular backdrop.
        if (i + frame * 2) % 5 == 0:
            continue
        if not boost and ry > 0.76:
            continue
        x = int(W * rx) + ((frame + i) % 3 - 1)
        y = int(H * ry) + ((frame * 3 + i) % 5 - 2)
        if size == 1:
            px[x, y] = color
        else:
            px[x, y] = color
            px[x + 1, y] = color
            px[x, y + 1] = color


def make_cell(source: Image.Image, frame: int, boost: bool) -> Image.Image:
    cell = Image.new("RGB", (W, H), GREEN)
    if boost:
        lengths = [968, 984, 1012, 956, 977, 994, 965]
        widths = [170, 176, 112, 172, 180, 174, 168]
        top = [28, 29, 24, 31, 28, 27, 30][frame]
    else:
        lengths = [756, 770, 872, 744, 778, 762, 750]
        widths = [146, 152, 108, 150, 158, 148, 144]
        top = [30, 31, 28, 32, 30, 29, 31][frame]

    plume = source.resize((widths[frame], lengths[frame]), Image.Resampling.NEAREST)
    plume = row_warp(plume, phase=frame * 0.83 + (0.35 if boost else 0), amplitude=5 if not boost else 6)
    plume = recolor_hot(plume, boost=boost, smear=frame == 2)
    x = (W - plume.width) // 2 + [-3, 2, 0, 3, -2, 1, -1][frame]
    y = top
    pp = plume.load()
    cp = cell.load()
    for py in range(plume.height):
        ty = y + py
        if not 0 <= ty < H:
            continue
        for px in range(plume.width):
            tx = x + px
            if 0 <= tx < W and pp[px, py] != GREEN:
                cp[tx, ty] = pp[px, py]
    add_sparks(cell, frame, boost)
    return cell


def make_sheet(source: Image.Image, boost: bool) -> Image.Image:
    sheet = Image.new("RGB", (W * FRAMES, H), GREEN)
    for frame in range(FRAMES):
        sheet.paste(make_cell(source, frame, boost), (frame * W, 0))
    return sheet


def save_preview(cruise: Image.Image, boost: Image.Image) -> None:
    preview = Image.new("RGB", (W * FRAMES, H * 2 + 32), (11, 11, 26))
    def composite(sheet: Image.Image, y: int) -> None:
        # Build an exact key mask: green pixels become transparent in preview.
        mask = Image.new("L", sheet.size, 0)
        spx, mpx = sheet.load(), mask.load()
        for yy in range(sheet.height):
            for xx in range(sheet.width):
                mpx[xx, yy] = 0 if spx[xx, yy] == GREEN else 255
        preview.paste(sheet, (0, y), mask)

    composite(cruise, 16)
    composite(boost, H + 16)
    preview.save(ROOT / "preview_j2.png")


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("usage: build_j2.py GENERATED_SOURCE.png")
    source_path = Path(sys.argv[1])
    if not source_path.exists():
        raise SystemExit(f"missing generated source: {source_path}")
    quantized = quantize(Image.open(source_path))
    box = content_bbox(quantized)
    source = quantized.crop(box)
    cruise = make_sheet(source, boost=False)
    boost = make_sheet(source, boost=True)
    cruise.save(ROOT / "flame_cruise.png")
    boost.save(ROOT / "flame_boost.png")
    save_preview(cruise, boost)
    print(f"source={source_path} quantized_bbox={box} crop={source.size}")
    print(f"saved {ROOT / 'flame_cruise.png'}")
    print(f"saved {ROOT / 'flame_boost.png'}")
    print(f"saved {ROOT / 'preview_j2.png'}")


if __name__ == "__main__":
    main()
