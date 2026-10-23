#!/usr/bin/env python3
"""Validate the three Ninja key poses and print the requested metrics."""

from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image


GREEN = (0, 255, 0)
EXPECTED_SIZE = (1024, 1024)
ROOT = Path(__file__).resolve().parent.parent
POSES = [ROOT / "key_state0.png", ROOT / "key_state1.png", ROOT / "key_state2.png"]


def load(path: Path) -> Image.Image:
    image = Image.open(path).convert("RGB")
    if image.size != EXPECTED_SIZE:
        raise AssertionError(f"{path.name}: expected 1024x1024, got {image.size}")
    return image


def mask(image: Image.Image, x: int, y: int) -> bool:
    return image.getpixel((x, y)) != GREEN


def bounds(image: Image.Image) -> tuple[int, int, int, int]:
    points = [(x, y) for y in range(1024) for x in range(1024) if mask(image, x, y)]
    if not points:
        raise AssertionError("image has no non-green pixels")
    xs, ys = zip(*points)
    return min(xs), min(ys), max(xs), max(ys)


def count_mask(image: Image.Image, box: tuple[int, int, int, int] | None = None) -> int:
    left, top, right, bottom = box or (0, 0, 1023, 1023)
    return sum(mask(image, x, y) for y in range(top, bottom + 1) for x in range(left, right + 1))


def color_difference(a: Image.Image, b: Image.Image, box: tuple[int, int, int, int] | None = None) -> int:
    left, top, right, bottom = box or (0, 0, 1023, 1023)
    return sum(a.getpixel((x, y)) != b.getpixel((x, y)) for y in range(top, bottom + 1) for x in range(left, right + 1))


def main() -> int:
    images = [load(path) for path in POSES]
    base = images[0]
    base_count = count_mask(base)
    print("pose         size       non-green bbox             silhouette px")
    for path, image in zip(POSES, images):
        print(f"{path.name:<12} {image.width}x{image.height}  {bounds(image)!s:<26} {count_mask(image):>10}")

    failed = False
    print("\ncomparison                         value       requirement")
    for name, image, threshold in (("state1", images[1], 4.0), ("state2", images[2], 15.0)):
        silhouette_mismatch = sum(mask(base, x, y) != mask(image, x, y) for y in range(1024) for x in range(1024))
        silhouette_pct = 100.0 * silhouette_mismatch / base_count
        colour_pct = 100.0 * color_difference(base, image) / (1024 * 1024)
        print(f"{name:<34} silhouette mismatch {silhouette_pct:7.3f}%  < 1.5%")
        print(f"{'':34} colour difference  {colour_pct:7.3f}%  > {threshold:.1f}%")
        failed |= silhouette_pct >= 1.5 or colour_pct <= threshold

    print("\nstate2 changed foreground by quadrant")
    quadrants = {
        "top": (0, 0, 1023, 511),
        "bottom": (0, 512, 1023, 1023),
        "left": (0, 0, 511, 1023),
        "right": (512, 0, 1023, 1023),
    }
    for name, box in quadrants.items():
        denominator = count_mask(base, box)
        changed = sum(
            mask(base, x, y) and base.getpixel((x, y)) != images[2].getpixel((x, y))
            for y in range(box[1], box[3] + 1)
            for x in range(box[0], box[2] + 1)
        )
        pct = 100.0 * changed / denominator if denominator else 0.0
        print(f"{name:<10} {pct:7.3f}%  > 8.0%")
        failed |= pct <= 8.0

    print("\nRESULT: " + ("FAIL" if failed else "PASS"))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
