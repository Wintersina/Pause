#!/usr/bin/env python3
"""Build the J2 JadePhantom flame sheets from generated painted source sheets."""

from __future__ import annotations

import argparse
import math
from pathlib import Path

from PIL import Image


KEY = (0, 255, 0)
PALETTE = (
    (14, 90, 68),    # deep green
    (34, 176, 122),  # jade
    (142, 240, 196), # mint
    (244, 234, 212), # warm bone-white
    (31, 181, 185),  # teal
)

CELL_W = 256
CELL_H = 1024
FRAME_COUNT = 8


def nearest_colour(rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    return min(PALETTE, key=lambda c: sum((a - b) ** 2 for a, b in zip(rgb, c)))


def is_key(rgb: tuple[int, int, int]) -> bool:
    # Key only chroma-green pixels close to #00FF00. All five flame colours
    # remain comfortably outside this distance and are therefore preserved.
    return math.dist(rgb, KEY) <= 96.0


def snap_and_key(image: Image.Image) -> Image.Image:
    source = image.convert("RGB")
    out = Image.new("RGB", source.size, KEY)
    src = source.load()
    dst = out.load()
    for y in range(source.height):
        for x in range(source.width):
            rgb = src[x, y]
            dst[x, y] = KEY if is_key(rgb) else nearest_colour(rgb)
    return out


def build_sheet(source_path: Path, output_path: Path) -> None:
    source = Image.open(source_path).convert("RGB")
    frames = []
    for frame in range(FRAME_COUNT):
        left = round(frame * source.width / FRAME_COUNT)
        right = round((frame + 1) * source.width / FRAME_COUNT)
        crop = source.crop((left, 0, right, source.height))
        # Nearest-neighbour only: retain hard pixel bands while fitting the
        # requested 256 x 1024 cell contract.
        resized = crop.resize((CELL_W, CELL_H), Image.Resampling.NEAREST)
        frames.append(snap_and_key(resized))

    sheet = Image.new("RGB", (CELL_W * FRAME_COUNT, CELL_H), KEY)
    for frame, image in enumerate(frames):
        sheet.paste(image, (frame * CELL_W, 0))
    output_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(output_path, format="PNG", optimize=False)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("cruise_source", type=Path)
    parser.add_argument("boost_source", type=Path)
    parser.add_argument("output_dir", type=Path)
    args = parser.parse_args()
    build_sheet(args.cruise_source, args.output_dir / "flame_cruise.png")
    build_sheet(args.boost_source, args.output_dir / "flame_boost.png")


if __name__ == "__main__":
    main()
