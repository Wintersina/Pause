#!/usr/bin/env python3
"""Build and audit the CrimsonHalo J2 flame flipbooks.

The optional --source-* arguments are used once to turn the generated source
art into deterministic project sprites. The normal invocation only audits the
two shipped sheets and refreshes the dark-background preview.
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
CRUISE = ROOT / "flame_cruise.png"
BOOST = ROOT / "flame_boost.png"
PREVIEW = ROOT / "preview_j2.png"

CELL_W = 256
CELL_H = 1024
FRAME_COUNT = 6
SHEET_SIZE = (CELL_W * FRAME_COUNT, CELL_H)
GREEN = (0, 255, 0)
PREVIEW_BG = (0x0B, 0x0B, 0x1A)
PALETTE = (
    (0x6E, 0x11, 0x36),  # deep wine
    (0xC8, 0x28, 0x5E),  # crimson-raspberry
    (0xFF, 0xB4, 0x3C),  # amber
    (0xF4, 0xEA, 0xD4),  # warm bone-white
    (0xFF, 0x7A, 0xA2),  # coral-rose
)
ALLOWED = set(PALETTE) | {GREEN}
HOLD_TICKS = (2, 2, 1, 2, 2, 2)


def rgb_distance(first: tuple[int, int, int], second: tuple[int, int, int]) -> int:
    return sum((a - b) ** 2 for a, b in zip(first, second))


def flat_pixels(image: Image.Image):
    getter = getattr(image, "get_flattened_data", None)
    return getter() if getter is not None else list(image.getdata())


def snap_colour(rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    # Generated chroma backgrounds can be a few values away from the requested
    # key. Keep the key stable before applying the five-colour nearest snap.
    if rgb[1] > 180 and rgb[0] < 80 and rgb[2] < 80:
        return GREEN
    return min(PALETTE, key=lambda colour: rgb_distance(rgb, colour))


def quantize(source: Path, destination: Path) -> None:
    image = Image.open(source).convert("RGB")
    if image.size != SHEET_SIZE:
        raise SystemExit(f"{source}: expected {SHEET_SIZE[0]}x{SHEET_SIZE[1]}, got {image.size}")
    pixels = [snap_colour(pixel) for pixel in flat_pixels(image)]
    image.putdata(pixels)
    image.save(destination, format="PNG", optimize=False)


def non_key_bbox(image: Image.Image, x0: int, x1: int):
    pixels = image.load()
    points = [
        (x, y)
        for y in range(CELL_H)
        for x in range(x0, x1)
        if pixels[x, y] != GREEN
    ]
    if not points:
        return None
    return min(x for x, _ in points), min(y for _, y in points), max(x for x, _ in points), max(y for _, y in points)


def audit(path: Path) -> tuple[Image.Image, list[str]]:
    image = Image.open(path).convert("RGB")
    failures: list[str] = []
    if image.size != SHEET_SIZE:
        failures.append(f"size {image.size}, expected {SHEET_SIZE}")
    colours = set(flat_pixels(image))
    unexpected = sorted(colours - ALLOWED)
    if unexpected:
        failures.append(f"unexpected colours {unexpected[:8]}")

    print(f"{path.name}: {image.width}x{image.height} | cells={FRAME_COUNT} | cell={CELL_W}x{CELL_H}")
    print("  frame  hold  bbox (x0,y0,x1,y1)          non-key-pixels")
    for frame in range(FRAME_COUNT):
        x0 = frame * CELL_W
        bbox = non_key_bbox(image, x0, x0 + CELL_W)
        count = sum(
            image.getpixel((x, y)) != GREEN
            for y in range(CELL_H)
            for x in range(x0, x0 + CELL_W)
        )
        print(f"  {frame:>5}  {HOLD_TICKS[frame]:>4}  {str(bbox):<29} {count:>14}")
        if bbox is None:
            failures.append(f"frame {frame} is empty")
    return image, failures


def make_preview(images: list[Image.Image]) -> None:
    preview = Image.new("RGB", (SHEET_SIZE[0], SHEET_SIZE[1] * len(images)), PREVIEW_BG)
    for index, image in enumerate(images):
        keyed = Image.new("RGB", image.size, PREVIEW_BG)
        source_pixels = image.load()
        keyed_pixels = keyed.load()
        for y in range(image.height):
            for x in range(image.width):
                if source_pixels[x, y] != GREEN:
                    keyed_pixels[x, y] = source_pixels[x, y]
        preview.paste(keyed, (0, index * SHEET_SIZE[1]))
    preview.save(PREVIEW, format="PNG", optimize=False)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-cruise", type=Path)
    parser.add_argument("--source-boost", type=Path)
    args = parser.parse_args()
    if (args.source_cruise is None) != (args.source_boost is None):
        parser.error("--source-cruise and --source-boost must be supplied together")
    if args.source_cruise is not None:
        quantize(args.source_cruise, CRUISE)
        quantize(args.source_boost, BOOST)

    images: list[Image.Image] = []
    failures: list[str] = []
    for path in (CRUISE, BOOST):
        if not path.exists():
            failures.append(f"missing {path.name}")
            continue
        image, image_failures = audit(path)
        images.append(image)
        failures.extend(f"{path.name}: {failure}" for failure in image_failures)

    if len(images) == 2:
        make_preview(images)
        print(f"preview: {PREVIEW.name} ({PREVIEW.stat().st_size} bytes)")
    if failures:
        raise SystemExit("FAIL: " + "; ".join(failures))
    print("PASS")


if __name__ == "__main__":
    main()
