#!/usr/bin/env python3
"""Validate the Sodium sheets against the approved 3x3 references."""

from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
REFERENCE_SIZE = (1536, 1536)
OUTPUT_SIZE = (3072, 3072)


def cell(image: Image.Image, col: int, row: int, size: int) -> Image.Image:
    return image.crop((col * size, row * size, (col + 1) * size, (row + 1) * size))


def pixels(image: Image.Image) -> list[tuple[int, int, int]]:
    return list(image.convert("RGB").getdata())


def mask(image: Image.Image) -> list[bool]:
    return [pixel != GREEN for pixel in pixels(image)]


def center(reference_cell: Image.Image) -> tuple[float, float]:
    points = [
        (x, y)
        for y in range(512)
        for x in range(512)
        if reference_cell.getpixel((x, y)) != GREEN
    ]
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    return ((min(xs) + max(xs)) / 2.0, (min(ys) + max(ys)) / 2.0)


def gem_indices(reference_cell: Image.Image) -> set[int]:
    cx, cy = center(reference_cell)
    return {
        y * 512 + x
        for y in range(512)
        for x in range(512)
        if (x - cx) ** 2 + (y - cy) ** 2 <= 45.0 * 45.0
    }


def compare(reference: Image.Image, output: Image.Image, row: int, col: int) -> tuple[float, float, float]:
    ref = cell(reference, col, row, 512).convert("RGB")
    out_hi = cell(output, col, row, 1024).convert("RGB")
    out = out_hi.resize((512, 512), Image.Resampling.NEAREST)
    ref_pixels = pixels(ref)
    out_pixels = pixels(out)
    ref_mask = mask(ref)
    out_mask = mask(out)
    silhouette_diff = 100.0 * sum(a != b for a, b in zip(ref_mask, out_mask)) / sum(ref_mask)
    gem = gem_indices(ref)
    gem_diff = 100.0 * sum(ref_pixels[i] != out_pixels[i] for i in gem) / len(gem)
    changed = 100.0 * sum(a != b for a, b in zip(ref_pixels, out_pixels)) / (512 * 512)
    return silhouette_diff, gem_diff, changed


def main() -> int:
    failed = False
    print("sheet                  cell  silhouette-diff  gem-diff  changed-pixels  requirements")
    print("---------------------  ----  ---------------  --------  --------------  ----------------")
    for state in range(3):
        ref_path = ROOT / f"skinref_state{state}.png"
        out_path = ROOT / f"skin_Sodium_state{state}.png"
        reference = Image.open(ref_path).convert("RGB")
        output = Image.open(out_path).convert("RGB")
        if reference.size != REFERENCE_SIZE:
            raise AssertionError(f"{ref_path.name}: expected {REFERENCE_SIZE}, got {reference.size}")
        if output.size != OUTPUT_SIZE:
            raise AssertionError(f"{out_path.name}: expected {OUTPUT_SIZE}, got {output.size}")
        for row in range(3):
            for col in range(3):
                silhouette, gem, changed = compare(reference, output, row, col)
                failed |= silhouette >= 1.5 or gem >= 6.0 or changed <= 12.0
                print(
                    f"{out_path.name:<21} {row * 3 + col + 1:>4}  "
                    f"{silhouette:>15.3f}%  {gem:>7.3f}%  {changed:>13.3f}%  "
                    f"<1.5 / <6 / >12"
                )
    print("\nRESULT: " + ("FAIL" if failed else "PASS"))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
