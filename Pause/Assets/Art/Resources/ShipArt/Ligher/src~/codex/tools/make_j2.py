#!/usr/bin/env python3
"""Build the constrained J2 flame sheets from generated source sheets."""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image


KEY = (0, 255, 0)
PALETTE = [
    (110, 48, 20),   # dark brown
    (200, 102, 46),  # copper
    (255, 180, 60),  # amber
    (244, 234, 212), # bone-white
    (110, 242, 238), # cyan
    KEY,
]
CELL_W = 256
CELL_H = 1024
CELLS = 6


def snap_to_palette(image: Image.Image) -> Image.Image:
    """Snap every RGB pixel to the six permitted colors."""
    # int16 overflows when squaring channel distances (255**2 > 32767).
    rgb = np.asarray(image.convert("RGB"), dtype=np.int32)
    colors = np.asarray(PALETTE, dtype=np.int32)
    distances = ((rgb[:, :, None, :] - colors[None, None, :, :]) ** 2).sum(axis=3)
    snapped = colors[distances.argmin(axis=2)].astype(np.uint8)
    return Image.fromarray(snapped, mode="RGB")


def content_bbox(cell: Image.Image) -> tuple[int, int, int, int]:
    arr = np.asarray(cell, dtype=np.uint8)
    mask = np.any(arr != np.asarray(KEY, dtype=np.uint8), axis=2)
    ys, xs = np.where(mask)
    if len(xs) == 0:
        raise ValueError("generated cell contains no plume")
    return int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())


def build_sheet(source: Path, output: Path, kind: str) -> None:
    source_image = snap_to_palette(Image.open(source))
    if source_image.size != (CELL_W * CELLS, CELL_H):
        raise ValueError(f"{source}: expected 1536x1024 source, got {source_image.size}")

    out = Image.new("RGB", (CELL_W * CELLS, CELL_H), KEY)
    # The source is a visual guide. These targets enforce the gameplay contract
    # and leave frame 2 visibly longer but narrower as a smear.
    if kind == "cruise":
        target_tails = [760, 752, 866, 756, 748, 758]
        width_scales = [1.00, 0.98, 0.70, 1.00, 0.98, 1.02]
    elif kind == "boost":
        target_tails = [872, 864, 992, 870, 864, 874]
        width_scales = [1.00, 0.98, 0.68, 1.00, 0.98, 1.02]
    else:
        raise ValueError(kind)

    top = 31  # approximately 3% down the portrait cell
    for index in range(CELLS):
        left = index * CELL_W
        cell = source_image.crop((left, 0, left + CELL_W, CELL_H))
        # Ensure no generated edge spill can violate the sprite-strip contract.
        cell_arr = np.asarray(cell).copy()
        cell_arr[0, :, :] = KEY
        cell_arr[-1, :, :] = KEY
        cell_arr[:, 0, :] = KEY
        cell_arr[:, -1, :] = KEY
        cell = Image.fromarray(cell_arr, mode="RGB")
        x0, y0, x1, y1 = content_bbox(cell)
        crop = cell.crop((x0, y0, x1 + 1, y1 + 1))

        target_h = target_tails[index] - top + 1
        natural_w = max(8, round(crop.width * target_h / crop.height))
        target_w = max(8, round(natural_w * width_scales[index]))
        resized = crop.resize((target_w, target_h), Image.Resampling.NEAREST)

        x = left + (CELL_W - target_w) // 2
        out.paste(resized, (x, top))

    if kind == "boost":
        # The generated boost art has the right silhouette but too much dark
        # material for the requested hotter state. Lift the stepped material
        # ramps while keeping a few dark/copper pixels for a readable flame body.
        lifted = np.asarray(out).copy()
        yy, xx = np.indices((CELL_H, CELL_W), dtype=np.float32)
        for index in range(CELLS):
            x0 = index * CELL_W
            cell = lifted[:, x0:x0 + CELL_W]
            center_distance = np.abs(xx - (CELL_W - 1) / 2) / (CELL_W / 2)
            upper = yy / CELL_H < 0.28
            dark = np.all(cell == (110, 48, 20), axis=2)
            copper = np.all(cell == (200, 102, 46), axis=2)
            amber = np.all(cell == (255, 180, 60), axis=2)
            # Use contiguous core bands rather than pixel dithering.
            cell[dark & (center_distance < 0.72) & upper] = (200, 102, 46)
            cell[copper & ((center_distance < 0.62) | upper)] = (255, 180, 60)
            cell[amber & (center_distance < 0.42)] = (244, 234, 212)
        out = Image.fromarray(lifted, mode="RGB")

    # Defense-in-depth: borders stay key green even if a future source changes.
    pixels = np.asarray(out).copy()
    for index in range(CELLS):
        x0 = index * CELL_W
        x1 = x0 + CELL_W - 1
        pixels[0, x0:x1 + 1] = KEY
        pixels[-1, x0:x1 + 1] = KEY
        pixels[:, x0] = KEY
        pixels[:, x1] = KEY
    Image.fromarray(pixels, mode="RGB").save(output)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--cruise-source", type=Path, required=True)
    parser.add_argument("--boost-source", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    build_sheet(args.cruise_source, args.output_dir / "flame_cruise.png", "cruise")
    build_sheet(args.boost_source, args.output_dir / "flame_boost.png", "boost")


if __name__ == "__main__":
    main()
