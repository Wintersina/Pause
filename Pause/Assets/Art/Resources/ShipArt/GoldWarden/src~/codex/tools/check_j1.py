#!/usr/bin/env python3
"""Validate the GoldWarden J1 pose set without changing any project files."""

from pathlib import Path

from PIL import Image
import numpy as np


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)
NAMES = ("key_state0.png", "key_state1.png", "key_state2.png")


def load(name: str) -> np.ndarray:
    path = ROOT / name
    with Image.open(path) as image:
        image = image.convert("RGBA")
        if image.size != (1024, 1024):
            raise ValueError(f"{name}: expected 1024x1024, got {image.size}")
        return np.array(image)


def non_green(image: np.ndarray) -> np.ndarray:
    return np.any(image[:, :, :3] != GREEN, axis=2)


def bbox(mask: np.ndarray) -> tuple[int, int, int, int] | None:
    ys, xs = np.where(mask)
    if not len(xs):
        return None
    return int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)


def pct(value: int, total: int) -> float:
    return 100.0 * value / total if total else 0.0


def main() -> int:
    images = [load(name) for name in NAMES]
    masks = [non_green(image) for image in images]
    base = images[0][:, :, :3]
    base_mask = masks[0]

    print("file             size       non-green bbox          silhouette px")
    print("---------------- ----------- ----------------------- ------------")
    for name, image, mask in zip(NAMES, images, masks):
        print(f"{name:<16} 1024x1024   {str(bbox(mask)):<23} {int(mask.sum()):>12}")

    print("\ncomparison                         measured     requirement")
    print("--------------------------------- ------------ ------------")
    passed = True
    for index, label in ((1, "state 1"), (2, "state 2")):
        silhouette_delta = int(np.count_nonzero(base_mask != masks[index]))
        silhouette_pct = pct(silhouette_delta, int(base_mask.sum()))
        color_delta = int(np.count_nonzero(np.any(base != images[index][:, :, :3], axis=2)))
        color_pct = pct(color_delta, base.shape[0] * base.shape[1])
        print(f"{label:<33} silhouette delta {silhouette_pct:>7.3f}%  < 1.5%")
        print(f"{'':33} color delta     {color_pct:>7.3f}%  > {4 if index == 1 else 14:.1f}%")
        passed &= silhouette_pct < 1.5
        passed &= color_pct > (4.0 if index == 1 else 14.0)

    state2_delta = np.any(base != images[2][:, :, :3], axis=2)
    left = base_mask & (np.indices(base_mask.shape)[1] < 512)
    right = base_mask & ~left
    left_pct = pct(int(np.count_nonzero(state2_delta & left)), int(left.sum()))
    right_pct = pct(int(np.count_nonzero(state2_delta & right)), int(right.sum()))
    print(f"{'state 2 left-half colour delta':<33} {left_pct:>7.3f}%  > 8.0%")
    print(f"{'state 2 right-half colour delta':<33} {right_pct:>7.3f}%  > 8.0%")
    passed &= left_pct > 8.0 and right_pct > 8.0

    print("\nRESULT: " + ("PASS" if passed else "FAIL"))
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
