#!/usr/bin/env python3
"""Validate J1 dimensions, framing, localized damage, and colour-difference budgets."""
from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image


GREEN = (0, 255, 0)
W = H = 1024


def load(path: Path) -> Image.Image:
    im = Image.open(path).convert("RGB")
    if im.size != (W, H):
        raise AssertionError(f"{path.name}: expected 1024x1024, got {im.size}")
    return im


def green(rgb: tuple[int, int, int]) -> bool:
    return rgb == GREEN


def masks(im: Image.Image) -> tuple[list[bool], list[tuple[int, int, int]]]:
    data = list(im.get_flattened_data())
    return [not green(p) for p in data], data


def bbox(mask: list[bool]) -> tuple[int, int, int, int]:
    points = [(i % W, i // W) for i, on in enumerate(mask) if on]
    return min(x for x, _ in points), min(y for _, y in points), max(x for x, _ in points), max(y for _, y in points)


def rect_mask(rects: list[tuple[int, int, int, int]]) -> list[bool]:
    out = [False] * (W * H)
    for x0, y0, x1, y1 in rects:
        for y in range(max(0, y0), min(H, y1)):
            row = y * W
            for x in range(max(0, x0), min(W, x1)):
                out[row + x] = True
    return out


def main() -> None:
    root = Path(sys.argv[1]) if len(sys.argv) == 2 else Path(__file__).resolve().parents[1]
    paths = [root / f"key_state{i}.png" for i in range(3)]
    ims = [load(p) for p in paths]
    ms, pixels = zip(*(masks(im) for im in ims))

    print("file          size       non-green bbox                 silhouette_px")
    for i, (path, mask) in enumerate(zip(paths, ms)):
        print(f"{path.name:14} 1024x1024   {bbox(mask)!s:30} {sum(mask):>8}")

    # These are the only state-1 replacement windows used by compose_j1.py.
    state1_zones = rect_mask([(90, 440, 325, 715), (590, 675, 830, 875)])

    print("\ncomparison                         silhouette_diff_pct  colour_diff_pct")
    for i in (1, 2):
        sil_diff = sum(a != b for a, b in zip(ms[0], ms[i])) / sum(ms[0]) * 100.0
        colour_diff = sum(a != b for a, b in zip(pixels[0], pixels[i])) / (W * H) * 100.0
        print(f"state0 -> state{i}                       {sil_diff:8.4f}%          {colour_diff:8.4f}%")
        assert sil_diff < 1.5, f"state{i}: silhouette difference >= 1.5%"
        if i == 1:
            assert colour_diff > 2.0, "state1: colour difference must exceed 2%"
            diff_indices = [j for j, (a, b) in enumerate(zip(pixels[0], pixels[1])) if a != b]
            assert all(state1_zones[j] for j in diff_indices), "state1: differences escaped zones (a)/(b)"
        else:
            assert colour_diff > 6.0, "state2: colour difference must exceed 6%"
            assert colour_diff < 35.0, "state2: colour difference must stay below 35%"

    print("\nPASS: dimensions, framing masks, silhouette budget, colour budgets, and state-1 zone containment")


if __name__ == "__main__":
    main()
