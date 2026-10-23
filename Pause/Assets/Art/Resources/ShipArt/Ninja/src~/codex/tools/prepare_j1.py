#!/usr/bin/env python3
"""Clean generated Ninja poses without changing their painted content.

The generated source is keyed to flat green, resized with nearest-neighbour only,
and the intact pose's keyed silhouette is used as the invariant outer mask.
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


GREEN = (0, 255, 0)
SIZE = 1024
TARGET_BBOX = (48, 48, 975, 975)


def greenish(pixel: tuple[int, int, int]) -> bool:
    r, g, b = pixel
    return r < 50 and g > 70 and b < 80 and g > r * 1.35 and g > b * 1.35


def keyed(path: Path) -> Image.Image:
    image = Image.open(path).convert("RGB")
    pixels = image.load()
    for y in range(image.height):
        for x in range(image.width):
            if greenish(pixels[x, y]):
                pixels[x, y] = GREEN
    return image


def bbox(image: Image.Image) -> tuple[int, int, int, int]:
    pixels = image.load()
    xs: list[int] = []
    ys: list[int] = []
    for y in range(image.height):
        for x in range(image.width):
            if pixels[x, y] != GREEN:
                xs.append(x)
                ys.append(y)
    if not xs:
        raise ValueError("source image contains no non-green pixels")
    return min(xs), min(ys), max(xs), max(ys)


def fit_to_old_frame(image: Image.Image, source_bbox: tuple[int, int, int, int]) -> Image.Image:
    left, top, right, bottom = source_bbox
    crop = image.crop((left, top, right + 1, bottom + 1))
    target_width = TARGET_BBOX[2] - TARGET_BBOX[0] + 1
    target_height = TARGET_BBOX[3] - TARGET_BBOX[1] + 1
    crop = crop.resize((target_width, target_height), Image.Resampling.NEAREST)
    result = Image.new("RGB", (SIZE, SIZE), GREEN)
    result.paste(crop, (TARGET_BBOX[0], TARGET_BBOX[1]))
    return result


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source_dir", type=Path)
    parser.add_argument("output_dir", type=Path)
    args = parser.parse_args()

    source_paths = [
        args.source_dir / "exec-03d50f17-a042-4f00-8b20-2b0b098c8f48.png",
        args.source_dir / "exec-263d7637-5558-46dc-8a93-75f7abf8a998.png",
        args.source_dir / "exec-f3bc0213-af6d-42d1-8bec-17e137f57d60.png",
    ]
    names = ["key_state0.png", "key_state1.png", "key_state2.png"]
    keyed_sources = [keyed(path) for path in source_paths]
    source_bbox = bbox(keyed_sources[0])
    fitted = [fit_to_old_frame(image, source_bbox) for image in keyed_sources]

    # The intact keyed silhouette is the contract for every pose. If an edit
    # accidentally erases an interior pixel, keep the intact pixel there rather
    # than introducing a green hole; any edit outside the silhouette is removed.
    intact = fitted[0]
    intact_pixels = intact.load()
    for image in fitted[1:]:
        pixels = image.load()
        for y in range(SIZE):
            for x in range(SIZE):
                if intact_pixels[x, y] == GREEN:
                    pixels[x, y] = GREEN
                elif pixels[x, y] == GREEN:
                    pixels[x, y] = intact_pixels[x, y]

    args.output_dir.mkdir(parents=True, exist_ok=True)
    for name, image in zip(names, fitted):
        image.save(args.output_dir / name, format="PNG", optimize=False)


if __name__ == "__main__":
    main()
