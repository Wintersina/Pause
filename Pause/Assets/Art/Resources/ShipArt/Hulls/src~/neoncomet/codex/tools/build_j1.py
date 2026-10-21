#!/usr/bin/env python3
"""Normalize generated NeonComet passes and build the J1 deliverables."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


GREEN = (0, 255, 0)
PREVIEW_BG = (0x0B, 0x0B, 0x1A)
TARGET_BOX = (48, 92, 975, 931)  # inclusive, matching the old NeonComet framing
TARGET_SIZE = (TARGET_BOX[2] - TARGET_BOX[0] + 1, TARGET_BOX[3] - TARGET_BOX[1] + 1)


def ship_mask(image: Image.Image) -> Image.Image:
    """Key the generator's green field without touching ship pixels."""
    rgb = image.convert("RGB")
    src = rgb.load()
    mask = Image.new("1", rgb.size, 0)
    dst = mask.load()
    for y in range(rgb.height):
        for x in range(rgb.width):
            r, g, b = src[x, y]
            green_field = g > 40 and g > 1.20 * r and g > 1.20 * b
            dst[x, y] = not green_field
    return mask


def normalize_source(path: Path) -> tuple[Image.Image, Image.Image]:
    image = Image.open(path).convert("RGB")
    mask = ship_mask(image)
    bbox = mask.getbbox()
    if bbox is None:
        raise ValueError(f"No ship pixels found in {path}")
    cropped = image.crop(bbox)
    cropped_mask = mask.crop(bbox)
    return (
        cropped.resize(TARGET_SIZE, Image.Resampling.NEAREST),
        cropped_mask.resize(TARGET_SIZE, Image.Resampling.NEAREST),
    )


def place(image: Image.Image, source_mask: Image.Image, fixed_mask: Image.Image, base: Image.Image | None) -> Image.Image:
    out = Image.new("RGB", (1024, 1024), GREEN)
    if base is not None:
        out.paste(base, (0, 0))
    pixels = out.load()
    source_pixels = image.load()
    mask_pixels = fixed_mask.load()
    source_mask_pixels = source_mask.load()
    x0, y0 = TARGET_BOX[0], TARGET_BOX[1]
    for y in range(TARGET_SIZE[1]):
        for x in range(TARGET_SIZE[0]):
            tx, ty = x0 + x, y0 + y
            if not mask_pixels[x, y]:
                continue
            if source_mask_pixels[x, y]:
                pixels[tx, ty] = source_pixels[x, y]
    return out


def preview(images: list[Image.Image], path: Path) -> None:
    canvas = Image.new("RGB", (3072, 1024), PREVIEW_BG)
    for index, image in enumerate(images):
        key = Image.new("RGB", image.size, PREVIEW_BG)
        mask = Image.new("1", image.size, 0)
        src = image.load()
        dst = mask.load()
        for y in range(image.height):
            for x in range(image.width):
                dst[x, y] = src[x, y] != GREEN
        key.paste(image, (0, 0), mask)
        canvas.paste(key, (index * 1024, 0))
    canvas.save(path, format="PNG", optimize=False)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--intact", type=Path, required=True)
    parser.add_argument("--damaged", type=Path, required=True)
    parser.add_argument("--wreck", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()

    args.out.mkdir(parents=True, exist_ok=True)
    intact, intact_mask = normalize_source(args.intact)
    damaged, damaged_mask = normalize_source(args.damaged)
    wreck, wreck_mask = normalize_source(args.wreck)

    state0 = place(intact, intact_mask, intact_mask, None)
    state1 = place(damaged, damaged_mask, intact_mask, state0)
    state2 = place(wreck, wreck_mask, intact_mask, state0)

    state0.save(args.out / "key_state0.png", format="PNG", optimize=False)
    state1.save(args.out / "key_state1.png", format="PNG", optimize=False)
    state2.save(args.out / "key_state2.png", format="PNG", optimize=False)
    preview([state0, state1, state2], args.out / "preview_j1.png")


if __name__ == "__main__":
    main()
