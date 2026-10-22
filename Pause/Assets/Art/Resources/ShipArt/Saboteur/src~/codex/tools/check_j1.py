#!/usr/bin/env python3
"""Validate the J1 Saboteur key poses and print the requested audit table."""

from __future__ import annotations

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)
FILES = [ROOT / "key_state0.png", ROOT / "key_state1.png", ROOT / "key_state2.png"]


def bbox_and_mask(im: Image.Image):
    px = im.load()
    coords = []
    for y in range(im.height):
        for x in range(im.width):
            if px[x, y] != GREEN:
                coords.append((x, y))
    if not coords:
        raise AssertionError("no non-green pixels")
    xs = [x for x, _ in coords]
    ys = [y for _, y in coords]
    return (min(xs), min(ys), max(xs), max(ys)), set(coords)


def main():
    images = []
    print("file          size       non-green bbox                 pixels")
    print("------------  ---------  ----------------------------  ------")
    for path in FILES:
        im = Image.open(path).convert("RGB")
        assert im.size == (1024, 1024), f"{path.name}: expected 1024x1024, got {im.size}"
        bbox, mask = bbox_and_mask(im)
        images.append((im, mask))
        print(f"{path.name:12}  {im.width}x{im.height}  {bbox!s:28}  {len(mask):6}")

    base, base_mask = images[0]
    base_bytes = base.tobytes()
    for i, (im, mask) in enumerate(images[1:], 1):
        silhouette_diff = len(base_mask ^ mask) / len(base_mask) * 100.0
        im_bytes = im.tobytes()
        colour_diff = sum(base_bytes[j:j + 3] != im_bytes[j:j + 3]
                          for j in range(0, len(base_bytes), 3)) / (1024 * 1024) * 100.0
        print(f"state0 vs state{i}: silhouette pixels differ {silhouette_diff:.3f}% | colour pixels differ {colour_diff:.3f}%")
        assert silhouette_diff < 1.5, f"state{i}: silhouette difference too high"
        if i == 1:
            assert 3.0 <= colour_diff <= 12.0, f"state1 colour diff out of range: {colour_diff:.3f}%"
        else:
            assert 8.0 <= colour_diff <= 20.0, f"state2 colour diff out of range: {colour_diff:.3f}%"
            state1_bytes = images[1][0].tobytes()
            state1_diff = sum(base_bytes[j:j + 3] != state1_bytes[j:j + 3]
                              for j in range(0, len(base_bytes), 3))
            assert colour_diff >= 1.5 * state1_diff / (1024 * 1024) * 100.0, "state2 is not 1.5x state1"
    print("PASS")


if __name__ == "__main__":
    main()
