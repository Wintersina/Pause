#!/usr/bin/env python3
"""Validate the generated IonLancer heavy-wreck sprite and make a preview."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "key_state0.png"
DAMAGED = ROOT / "key_state1.png"
WRECK = ROOT / "key_state2_v2.png"
PREVIEW = ROOT / "preview_wreck.png"
GREEN = np.array([0, 255, 0], dtype=np.uint8)


def rgb(path: Path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGB"), dtype=np.uint8)


def percent(numerator: int, denominator: int) -> float:
    return 100.0 * numerator / denominator if denominator else 0.0


def main() -> int:
    source = rgb(SOURCE)
    damaged = rgb(DAMAGED)
    wreck = rgb(WRECK)
    source_mask = np.any(source != GREEN, axis=2)
    wreck_mask = np.any(wreck != GREEN, axis=2)

    if wreck.shape != (1024, 1024, 3):
        raise SystemExit(f"key_state2_v2.png must be 1024x1024 RGB, got {wreck.shape}")

    mask_changed = np.logical_xor(source_mask, wreck_mask)
    source_diff = np.abs(source.astype(np.int16) - wreck.astype(np.int16)).sum(axis=2)
    damaged_diff = np.abs(damaged.astype(np.int16) - wreck.astype(np.int16)).sum(axis=2)
    changed = source_diff > 60

    left_hull = source_mask & (np.indices(source_mask.shape)[1] < 512)
    right_hull = source_mask & (np.indices(source_mask.shape)[1] >= 512)

    rows = [
        ("dimensions", "1024 x 1024", "PASS"),
        (
            "silhouette pixels differing from key_state0",
            f"{percent(int(mask_changed.sum()), int(source_mask.sum())):.3f}%",
            "PASS" if percent(int(mask_changed.sum()), int(source_mask.sum())) < 0.5 else "FAIL",
        ),
        (
            "colour-different pixels vs key_state0 (sum RGB > 60)",
            f"{percent(int((source_diff > 60).sum()), source_diff.size):.3f}%",
            "PASS" if percent(int((source_diff > 60).sum()), source_diff.size) > 22 else "FAIL",
        ),
        (
            "colour-different pixels vs key_state1 (sum RGB > 60)",
            f"{percent(int((damaged_diff > 60).sum()), damaged_diff.size):.3f}%",
            "PASS" if percent(int((damaged_diff > 60).sum()), damaged_diff.size) > 12 else "FAIL",
        ),
        (
            "damaged-region pixels, LEFT hull half",
            f"{percent(int((changed & left_hull).sum()), int(left_hull.sum())):.3f}%",
            "PASS" if percent(int((changed & left_hull).sum()), int(left_hull.sum())) > 15 else "FAIL",
        ),
        (
            "damaged-region pixels, RIGHT hull half",
            f"{percent(int((changed & right_hull).sum()), int(right_hull.sum())):.3f}%",
            "PASS" if percent(int((changed & right_hull).sum()), int(right_hull.sum())) > 15 else "FAIL",
        ),
    ]

    print("IonLancer wreck validation")
    print("=" * 78)
    print(f"{'metric':58} {'value':12} result")
    print("-" * 78)
    for metric, value, result in rows:
        print(f"{metric:58} {value:12} {result}")

    preview_bg = Image.new("RGB", (3072, 1024), (11, 11, 26))
    for index, image in enumerate((SOURCE, DAMAGED, WRECK)):
        sprite_array = np.asarray(Image.open(image).convert("RGB"), dtype=np.uint8).copy()
        sprite_array[np.all(sprite_array == GREEN, axis=2)] = (11, 11, 26)
        sprite = Image.fromarray(sprite_array, "RGB")
        preview_bg.paste(sprite, (index * 1024, 0))
    draw = ImageDraw.Draw(preview_bg)
    draw.rectangle((0, 0, 3071, 1023), outline=(35, 35, 60), width=2)
    preview_bg.save(PREVIEW, format="PNG", optimize=False)
    print(f"preview: {PREVIEW}")

    return 0 if all(row[2] == "PASS" for row in rows) else 1


if __name__ == "__main__":
    raise SystemExit(main())
