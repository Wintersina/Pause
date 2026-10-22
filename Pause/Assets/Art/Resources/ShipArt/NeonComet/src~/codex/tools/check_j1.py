#!/usr/bin/env python3
"""Validate the NeonComet J1 dimensions, mask lock, and damage deltas."""

from __future__ import annotations

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)
FILES = [ROOT / "key_state0.png", ROOT / "key_state1.png", ROOT / "key_state2.png"]


def mask(image: Image.Image) -> list[bool]:
    return [pixel != GREEN for pixel in image.getdata()]


def bbox(bits: list[bool], width: int, height: int) -> tuple[int, int, int, int] | None:
    points = [(i % width, i // width) for i, value in enumerate(bits) if value]
    if not points:
        return None
    xs = [x for x, _ in points]
    ys = [y for _, y in points]
    return min(xs), min(ys), max(xs), max(ys)


def percent(value: int, total: int) -> float:
    return 100.0 * value / total if total else 0.0


def main() -> int:
    images = [Image.open(path).convert("RGB") for path in FILES]
    errors: list[str] = []
    for path, image in zip(FILES, images):
        if image.size != (1024, 1024):
            errors.append(f"{path.name}: expected 1024x1024, got {image.size}")

    width, height = images[0].size
    masks = [mask(image) for image in images]
    print("file          size       non-green bbox       silhouette px")
    for path, image, bits in zip(FILES, images, masks):
        print(f"{path.name:<13} {image.width}x{image.height}  {bbox(bits, width, height)}  {sum(bits)}")

    for index, label in ((1, "state1"), (2, "state2")):
        silhouette_delta = sum(a != b for a, b in zip(masks[0], masks[index]))
        color_delta = sum(a != b for a, b in zip(images[0].getdata(), images[index].getdata()))
        silhouette_pct = percent(silhouette_delta, len(masks[0]))
        color_pct = percent(color_delta, width * height)
        print(f"state0 vs {label}: silhouette differs {silhouette_pct:.3f}% (must be < 1.5%), color differs {color_pct:.3f}% (must be > {3 if index == 1 else 8}%)")
        if silhouette_pct >= 1.5:
            errors.append(f"state0 vs {label}: silhouette delta is {silhouette_pct:.3f}%")
        if color_pct <= (3 if index == 1 else 8):
            errors.append(f"state0 vs {label}: color delta is {color_pct:.3f}%")

    if errors:
        print("FAIL")
        for error in errors:
            print(f"- {error}")
        return 1
    print("PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
