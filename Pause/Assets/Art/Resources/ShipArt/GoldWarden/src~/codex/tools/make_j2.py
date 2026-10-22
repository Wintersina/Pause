#!/usr/bin/env python3
"""Build J2 GoldWarden flame sheets from generated painted sources."""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image


GREEN = (0, 255, 0)
PALETTE = np.array(
    [
        (126, 78, 12),   # dark brown
        (217, 154, 26),  # amber gold
        (255, 211, 106), # light gold
        (244, 234, 212), # warm bone-white
        (216, 35, 44),   # red
        GREEN,
    ],
    dtype=np.int32,
)


def snap(image: Image.Image) -> np.ndarray:
    rgb = np.asarray(image.convert("RGB"), dtype=np.int32)
    delta = rgb[:, :, None, :] - PALETTE[None, None, :, :]
    distance = np.sum(delta * delta, axis=3)
    return PALETTE[np.argmin(distance, axis=2)].astype(np.uint8)


def source_cells(path: Path) -> list[np.ndarray]:
    source = Image.open(path).convert("RGB")
    width, height = source.size
    edges = [round(i * width / 6) for i in range(7)]
    cells = []
    for i in range(6):
        crop = source.crop((edges[i], 0, edges[i + 1], height))
        cells.append(snap(crop))
    return cells


def flame_bbox(cell: np.ndarray) -> tuple[int, int, int, int]:
    mask = np.any(cell != np.array(GREEN, dtype=np.uint8), axis=2)
    ys, xs = np.where(mask)
    if len(xs) == 0:
        raise ValueError("generated source cell contains no flame pixels")
    return int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)


def place_cell(cell: np.ndarray, width: int, height: int, y: int) -> np.ndarray:
    left, top, right, bottom = flame_bbox(cell)
    cropped = Image.fromarray(cell[top:bottom, left:right], mode="RGB")
    scaled = cropped.resize((width, height), Image.Resampling.NEAREST)
    canvas = Image.new("RGB", (256, 1024), GREEN)
    x = (256 - width) // 2
    canvas.paste(scaled, (x, y))
    return np.asarray(canvas, dtype=np.uint8).copy()


def ensure_extra_boost_sparks(frame: np.ndarray, index: int) -> None:
    """Keep the generated boost read as a hotter, sparkier state."""
    red = np.array((216, 35, 44), dtype=np.uint8)
    green = np.array(GREEN, dtype=np.uint8)
    spots = [
        (31 + index * 9, 145 + (index * 37) % 170),
        (222 - index * 7, 225 + (index * 29) % 190),
        (46 + index * 5, 420 + (index * 23) % 210),
        (209 - index * 6, 520 + (index * 31) % 180),
    ]
    for x, y in spots:
        if frame[y, x].tolist() == green.tolist():
            frame[y, x] = red
            if x + 1 < 256 and frame[y, x + 1].tolist() == green.tolist():
                frame[y, x + 1] = red


def build(source: Path, output: Path, boost: bool) -> None:
    cells = source_cells(source)
    if boost:
        widths = [150, 158, 142, 156, 160, 152]
        heights = [950, 960, 975, 955, 945, 960]
        top = 24
    else:
        widths = [116, 124, 108, 120, 114, 122]
        heights = [680, 700, 795, 690, 675, 700]
        top = 29

    frames = []
    for i, cell in enumerate(cells):
        frame = place_cell(cell, widths[i], heights[i], top)
        if boost:
            ensure_extra_boost_sparks(frame, i)
        frames.append(frame)

    sheet = np.concatenate(frames, axis=1)
    Image.fromarray(sheet, mode="RGB").save(output)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("cruise_source", type=Path)
    parser.add_argument("boost_source", type=Path)
    parser.add_argument("output_dir", type=Path)
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    build(args.cruise_source, args.output_dir / "flame_cruise.png", boost=False)
    build(args.boost_source, args.output_dir / "flame_boost.png", boost=True)


if __name__ == "__main__":
    main()
