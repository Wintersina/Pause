#!/usr/bin/env python3
"""Build the J2 flame sheets from generated painted source strips.

The generated art is only a paint source. This export step owns the exact
sheet geometry, nearest-neighbour resampling, and the closed J2 palette.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image


PALETTE = np.array(
    [
        (23, 76, 140),    # deep blue
        (46, 155, 230),   # azure
        (110, 242, 238),  # cyan
        (244, 234, 212),  # warm bone-white
        (242, 134, 43),   # orange
        (0, 255, 0),      # key green
    ],
    dtype=np.int16,
)
GREEN = np.array((0, 255, 0), dtype=np.uint8)
CELL_SIZE = (256, 1024)
FRAME_COUNT = 7
# Preserve the generated kink changes, but normalize the vertical contract so
# source-model framing cannot make a frame look short by accident. Frame 2 is
# deliberately the stretched smear.
CRUISE_TAILS = (0.82, 0.85, 0.94, 0.82, 0.88, 0.80, 0.86)
BOOST_TAILS = (0.95, 0.96, 0.97, 0.95, 0.96, 0.94, 0.95)


def quantize(source: Image.Image) -> Image.Image:
    rgb = np.asarray(source.convert("RGB"), dtype=np.int16)

    # Generated green is the matte, not painted content. Clean its small
    # compression/edge variations before nearest-palette snapping so the
    # exported background remains a genuinely flat key.
    r, g, b = rgb.transpose(2, 0, 1)
    greenish = (g > 140) & (g > r * 1.35) & (g > b * 1.10)
    rgb[greenish] = GREEN

    # Use a wide signed type: int16 would overflow while squaring channel
    # distances such as 255, producing visibly wrong nearest colours.
    distances = ((rgb.astype(np.int32)[:, :, None, :] - PALETTE.astype(np.int32)[None, None, :, :]) ** 2).sum(axis=3)
    indices = distances.argmin(axis=2)
    return Image.fromarray(PALETTE[indices].astype(np.uint8), mode="RGB")


def fit_vertical(image: Image.Image, tail_fraction: float) -> Image.Image:
    """Move the painted flame to a consistent head/tail range, pixel-clean."""
    pixels = np.asarray(image)
    mask = np.any(pixels != GREEN, axis=2)
    ys, _ = np.where(mask)
    if len(ys) == 0:
        return image
    y0, y1 = int(ys.min()), int(ys.max() + 1)
    painted = image.crop((0, y0, image.width, y1))
    top = round(image.height * 0.03)
    bottom = round(image.height * tail_fraction)
    resized = painted.resize((image.width, max(1, bottom - top)), Image.Resampling.NEAREST)
    fitted = Image.new("RGB", image.size, tuple(GREEN))
    fitted.paste(resized, (0, top))
    return fitted


def build(source_path: Path, output_path: Path) -> None:
    source = Image.open(source_path).convert("RGB")
    frames: list[Image.Image] = []
    tails = BOOST_TAILS if "boost" in output_path.stem else CRUISE_TAILS
    for frame in range(FRAME_COUNT):
        x0 = round(frame * source.width / FRAME_COUNT)
        x1 = round((frame + 1) * source.width / FRAME_COUNT)
        cell = source.crop((x0, 0, x1, source.height))
        cell = cell.resize(CELL_SIZE, Image.Resampling.NEAREST)
        frames.append(fit_vertical(quantize(cell), tails[frame]))

    sheet = Image.new("RGB", (CELL_SIZE[0] * FRAME_COUNT, CELL_SIZE[1]), tuple(GREEN))
    for frame, image in enumerate(frames):
        sheet.paste(image, (frame * CELL_SIZE[0], 0))
    output_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(output_path, format="PNG", optimize=False)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("cruise_source", type=Path)
    parser.add_argument("boost_source", type=Path)
    parser.add_argument("output_dir", type=Path)
    args = parser.parse_args()
    build(args.cruise_source, args.output_dir / "flame_cruise.png")
    build(args.boost_source, args.output_dir / "flame_boost.png")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
