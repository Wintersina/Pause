#!/usr/bin/env python3
"""Validate the three VoltViper J1 key poses and print the requested table."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image


GREEN = np.array([0, 255, 0], dtype=np.uint8)
ROOT = Path(__file__).resolve().parents[1]


def load(path: Path) -> np.ndarray:
    image = Image.open(path).convert("RGB")
    return np.array(image)


def mask(image: np.ndarray) -> np.ndarray:
    return np.any(image != GREEN, axis=2)


def bounds(m: np.ndarray) -> tuple[int, int, int, int]:
    ys, xs = np.where(m)
    if not len(xs):
        return (0, 0, 0, 0)
    return (int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1))


def main() -> int:
    names = ("key_state0.png", "key_state1.png", "key_state2.png")
    images = [load(ROOT / name) for name in names]
    masks = [mask(image) for image in images]
    base = images[0]
    base_mask = masks[0]

    print("file            size        non-green bbox                 silhouette px   colour px")
    print("---------------- ----------- ----------------------------- --------------- -------------")
    for name, image, m in zip(names, images, masks):
        silhouette_diff = np.mean(m != base_mask) * 100.0
        colour_diff = np.mean(np.any(image != base, axis=2)) * 100.0
        w, h = image.shape[1], image.shape[0]
        print(f"{name:<16} {w:4}x{h:<6} {str(bounds(m)):29} {silhouette_diff:12.3f}% {colour_diff:10.3f}%")

    ok = True
    ok &= all(image.shape == (1024, 1024, 3) for image in images)
    ok &= all(np.mean(m != base_mask) < 0.015 for m in masks[1:])
    ok &= np.mean(np.any(images[1] != base, axis=2)) > 0.03
    ok &= np.mean(np.any(images[2] != base, axis=2)) > 0.08
    print("\nJ1 CHECK: " + ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
