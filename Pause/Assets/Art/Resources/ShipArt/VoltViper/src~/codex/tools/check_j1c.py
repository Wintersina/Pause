#!/usr/bin/env python3
"""Validate the J1c VoltViper damaged poses."""

from __future__ import annotations

from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image


GREEN = np.array([0, 255, 0], dtype=np.uint8)
ROOT = Path(__file__).resolve().parents[1]


def load(path: Path) -> np.ndarray:
    image = Image.open(path).convert("RGB")
    assert image.size == (1024, 1024), f"{path.name}: expected 1024x1024, got {image.size}"
    return np.array(image)


def silhouette(image: np.ndarray) -> np.ndarray:
    return np.any(image != GREEN, axis=2)


def bounds(mask: np.ndarray) -> tuple[int, int, int, int]:
    ys, xs = np.where(mask)
    return int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)


def greenish(image: np.ndarray) -> np.ndarray:
    r, g, b = image.astype(np.int16).transpose(2, 0, 1)
    return (g > 100) & (g > 1.55 * r) & (g > 1.40 * b)


def background_connected(green_pixels: np.ndarray) -> np.ndarray:
    height, width = green_pixels.shape
    connected = np.zeros_like(green_pixels, dtype=bool)
    queue: deque[tuple[int, int]] = deque()
    for x in range(width):
        if green_pixels[0, x]:
            queue.append((0, x))
        if green_pixels[height - 1, x]:
            queue.append((height - 1, x))
    for y in range(height):
        if green_pixels[y, 0]:
            queue.append((y, 0))
        if green_pixels[y, width - 1]:
            queue.append((y, width - 1))
    while queue:
        y, x = queue.popleft()
        if connected[y, x] or not green_pixels[y, x]:
            continue
        connected[y, x] = True
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dy == 0 and dx == 0:
                    continue
                ny, nx = y + dy, x + dx
                if 0 <= ny < height and 0 <= nx < width and not connected[ny, nx]:
                    queue.append((ny, nx))
    return connected


def interior_green_count(image: np.ndarray, ship_bbox: tuple[int, int, int, int]) -> int:
    x0, y0, x1, y1 = ship_bbox
    # Exact #00FF00 is the declared background, even where the bbox encloses a
    # background pocket between ship parts. Only green-ish non-background pixels
    # can be contamination on the painted hull.
    green_pixels = greenish(image) & silhouette(image)
    connected = background_connected(green_pixels)
    return int(np.count_nonzero(green_pixels[y0:y1, x0:x1] & ~connected[y0:y1, x0:x1]))


def main() -> int:
    names = ("key_state0.png", "key_state1.png", "key_state2.png")
    images = [load(ROOT / name) for name in names]
    masks = [silhouette(image) for image in images]
    base = images[0]
    base_mask = masks[0]
    ship_bbox = bounds(base_mask)

    print("file            size        silhouette diff   colour diff    internal green-ish")
    print("---------------- ----------- ----------------- ------------- -------------------")
    colour_diffs: list[float] = []
    for name, image, mask in zip(names, images, masks):
        silhouette_diff = float(np.mean(mask != base_mask) * 100.0)
        colour_diff = float(np.mean(np.any(image != base, axis=2)) * 100.0)
        colour_diffs.append(colour_diff)
        green_count = interior_green_count(image, ship_bbox)
        print(f"{name:<16} 1024x1024       {silhouette_diff:8.3f}%       {colour_diff:8.3f}% {green_count:19d}")

    assert all(mask.shape == base_mask.shape for mask in masks)
    assert all(np.array_equal(mask, base_mask) for mask in masks[1:]), "silhouette changed"
    assert colour_diffs[1] > 3.0, "state 1 colour difference must exceed 3%"
    assert colour_diffs[2] > 8.0, "state 2 colour difference must exceed 8%"
    assert colour_diffs[2] > colour_diffs[1], "state 2 must differ more than state 1"
    assert all(interior_green_count(image, ship_bbox) == 0 for image in images), "green on ship"
    print("\nJ1C CHECK: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
