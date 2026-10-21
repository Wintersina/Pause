#!/usr/bin/env python3
"""Build J2 flame sheets from imagegen output using the exact six-color key palette."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


CELL_W = 256
CELL_H = 1024
FRAME_COUNT = 6
SHEET_SIZE = (CELL_W * FRAME_COUNT, CELL_H)
PALETTE = (
    (0x86, 0x12, 0x1F),  # deep red
    (0xD8, 0x23, 0x2C),  # red
    (0xF2, 0x86, 0x2B),  # orange
    (0xF4, 0xEA, 0xD4),  # warm bone-white
    (0x6E, 0xF2, 0xEE),  # cyan
    (0x00, 0xFF, 0x00),  # key green
)


def snap_to_palette(image: Image.Image) -> Image.Image:
    """Map every pixel to the fixed palette with no dithering."""
    image = image.convert("RGB")
    if image.size != SHEET_SIZE:
        image = image.resize(SHEET_SIZE, Image.Resampling.NEAREST)
    source = image.load()
    output = Image.new("RGB", image.size)
    target = output.load()
    nearest_cache: dict[tuple[int, int, int], tuple[int, int, int]] = {}
    for y in range(image.height):
        for x in range(image.width):
            pixel = source[x, y]
            mapped = nearest_cache.get(pixel)
            if mapped is None:
                mapped = min(
                    PALETTE,
                    key=lambda color: sum((pixel[channel] - color[channel]) ** 2 for channel in range(3)),
                )
                nearest_cache[pixel] = mapped
            target[x, y] = mapped
    return output


def build(source: Path, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    snapped = snap_to_palette(Image.open(source))
    snapped.save(destination, format="PNG", optimize=False)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--cruise", type=Path, required=True)
    parser.add_argument("--boost", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    build(args.cruise, args.out / "flame_cruise.png")
    build(args.boost, args.out / "flame_boost.png")


if __name__ == "__main__":
    main()
