"""Build the J2 Dove flame sheets from the generated painted references.

The generated references are intentionally treated as paint/layout input only.  The
export path keys the green, resizes with nearest-neighbour, and snaps every remaining
pixel to the five requested flame colours.
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


GREEN = (0, 255, 0)
PALETTE = [
    (0x14, 0x6A, 0x70),  # dark teal
    (0x36, 0xC2, 0xC2),  # aqua
    (0xA6, 0xF2, 0xEE),  # light aqua
    (0xF4, 0xEA, 0xD4),  # warm bone-white
    (0xFF, 0xB4, 0x3C),  # amber
]

CELL = (256, 1024)
HEAD_Y = 31


def is_green(pixel: tuple[int, int, int]) -> bool:
    r, g, b = pixel
    return g >= 175 and g > r * 1.25 + 8 and g > b * 1.25 + 8


def nearest(pixel: tuple[int, int, int]) -> tuple[int, int, int]:
    return min(PALETTE, key=lambda colour: sum((a - b) ** 2 for a, b in zip(pixel, colour)))


def key_source(source: Image.Image) -> Image.Image:
    source = source.convert("RGB")
    keyed = Image.new("RGB", source.size, GREEN)
    src = source.load()
    dst = keyed.load()
    for y in range(source.height):
        for x in range(source.width):
            pixel = src[x, y]
            if not is_green(pixel):
                dst[x, y] = pixel
    return keyed


def content_bbox(image: Image.Image) -> tuple[int, int, int, int]:
    px = image.load()
    points = [(x, y) for y in range(image.height) for x in range(image.width) if px[x, y] != GREEN]
    if not points:
        raise ValueError("generated frame contains no non-green pixels")
    return min(x for x, _ in points), min(y for _, y in points), max(x for x, _ in points), max(y for _, y in points)


def quantize(image: Image.Image) -> Image.Image:
    out = Image.new("RGB", image.size, GREEN)
    src = image.load()
    dst = out.load()
    for y in range(image.height):
        for x in range(image.width):
            pixel = src[x, y]
            dst[x, y] = GREEN if pixel == GREEN else nearest(pixel)
    return out


def make_frame(source_cell: Image.Image, width: int, tail_y: int) -> Image.Image:
    bbox = content_bbox(source_cell)
    crop = source_cell.crop((bbox[0], bbox[1], bbox[2] + 1, bbox[3] + 1))
    height = tail_y - HEAD_Y + 1
    scaled = crop.resize((width, height), Image.Resampling.NEAREST)
    frame = Image.new("RGB", CELL, GREEN)
    x = (CELL[0] - width) // 2
    frame.paste(scaled, (x, HEAD_Y))
    return quantize(frame)


def make_smear(source_cell: Image.Image, width: int, tail_y: int) -> Image.Image:
    """Make the one-tick smear narrow and visually near-solid white."""
    frame = make_frame(source_cell, width, tail_y)
    px = frame.load()
    for y in range(frame.height):
        for x in range(frame.width):
            if px[x, y] not in (GREEN, (0xFF, 0xB4, 0x3C)):
                px[x, y] = (0xF4, 0xEA, 0xD4)
    return frame


def build_sheet(source_path: Path, output_path: Path, widths: list[int], tails: list[float]) -> None:
    source = key_source(Image.open(source_path))
    strip_width = source.width / 7.0
    frames = []
    for index, (width, tail_fraction) in enumerate(zip(widths, tails)):
        left = round(index * strip_width)
        right = round((index + 1) * strip_width)
        cell = source.crop((left, 0, right, source.height))
        tail_y = round((CELL[1] - 1) * tail_fraction)
        maker = make_smear if index == 2 else make_frame
        frames.append(maker(cell, width, tail_y))

    sheet = Image.new("RGB", (CELL[0] * 7, CELL[1]), GREEN)
    for index, frame in enumerate(frames):
        sheet.paste(frame, (index * CELL[0], 0))
    sheet.save(output_path, format="PNG", optimize=False)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("cruise_source", type=Path)
    parser.add_argument("boost_source", type=Path)
    parser.add_argument("output_dir", type=Path)
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)

    build_sheet(
        args.cruise_source,
        args.output_dir / "flame_cruise.png",
        [190, 196, 82, 190, 196, 194, 190],
        [0.78, 0.82, 0.92, 0.79, 0.83, 0.80, 0.77],
    )
    build_sheet(
        args.boost_source,
        args.output_dir / "flame_boost.png",
        [225, 232, 92, 228, 234, 228, 232],
        [0.93, 0.96, 0.99, 0.94, 0.97, 0.95, 0.96],
    )


if __name__ == "__main__":
    main()
