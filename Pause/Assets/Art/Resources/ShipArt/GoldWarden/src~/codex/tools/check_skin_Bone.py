#!/usr/bin/env python3
"""Audit Bone sheets against the approved 3x3 reference flipbooks."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)
REFERENCES = tuple(ROOT / f"skinref_state{i}.png" for i in range(3))
OUTPUTS = tuple(ROOT / f"skin_Bone_state{i}.png" for i in range(3))


def hsv(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    value = rgb.max(axis=2)
    minimum = rgb.min(axis=2)
    delta = value - minimum
    saturation = np.divide(delta, value, out=np.zeros_like(value), where=value != 0)
    hue = np.zeros_like(value)
    nonzero = delta != 0
    red, green, blue = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    red_case = nonzero & (value == red)
    green_case = nonzero & (value == green) & ~red_case
    blue_case = nonzero & (value == blue) & ~red_case & ~green_case
    hue[red_case] = ((green[red_case] - blue[red_case]) / delta[red_case]) % 6
    hue[green_case] = (blue[green_case] - red[green_case]) / delta[green_case] + 2
    hue[blue_case] = (red[blue_case] - green[blue_case]) / delta[blue_case] + 4
    return hue * 60.0, saturation, value


def load(path: Path, size: tuple[int, int]) -> np.ndarray:
    with Image.open(path) as image:
        array = np.asarray(image.convert("RGB"), dtype=np.uint8)
    if array.shape[:2] != size:
        raise ValueError(f"{path.name}: expected {size[0]}x{size[1]}, got {array.shape[:2]}")
    return array


def cell(array: np.ndarray, index: int) -> np.ndarray:
    row, column = divmod(index, 3)
    return array[row * 512:(row + 1) * 512, column * 512:(column + 1) * 512]


def percent(count: int, total: int) -> float:
    return 100.0 * count / total if total else 0.0


def blue_mask(reference: np.ndarray) -> np.ndarray:
    hue, saturation, value = hsv(reference.astype(np.float32) / 255.0)
    blue = (((hue >= 150.0) & (hue <= 270.0)) | ((hue >= 170.0) & (hue <= 230.0)))
    return blue & (saturation > 0.16) & (value > 0.08)


def main() -> int:
    passed = True
    print("file                 cell  silhouette Δ   canopy/blue Δ   changed pixels   requirements")
    print("-------------------- ----- --------------- --------------- ---------------  ---------------------------")
    for reference_path, output_path in zip(REFERENCES, OUTPUTS):
        reference = load(reference_path, (1536, 1536))
        output_big = load(output_path, (3072, 3072))
        output = output_big[::2, ::2]
        reference_green = np.all(reference == GREEN, axis=2)
        background_ok = bool(np.all(output[reference_green] == GREEN))
        passed &= background_ok
        for index in range(9):
            ref_cell = cell(reference, index)
            out_cell = cell(output, index)
            ref_non_green = np.any(ref_cell != GREEN, axis=2)
            out_non_green = np.any(out_cell != GREEN, axis=2)
            silhouette_delta = percent(int(np.count_nonzero(ref_non_green != out_non_green)), int(ref_non_green.sum()))
            blue = blue_mask(ref_cell)
            canopy_delta = percent(int(np.count_nonzero(np.any(ref_cell[blue] != out_cell[blue], axis=1))), int(blue.sum()))
            changed = percent(int(np.count_nonzero(np.any(ref_cell != out_cell, axis=2))), ref_cell.shape[0] * ref_cell.shape[1])
            changed_ok = changed > 12.0 if index != 8 else changed == 0.0
            ok = silhouette_delta < 1.5 and canopy_delta < 6.0 and changed_ok
            passed &= ok
            requirement = ">12.0%" if index != 8 else "0.0% approved flash"
            print(f"{output_path.name:<20} {index:>4}  {silhouette_delta:>12.3f}%  {canopy_delta:>12.3f}%  {changed:>12.3f}%   {'PASS' if ok else 'FAIL'} ({requirement})")

        output_hue, output_saturation, _ = hsv(output.astype(np.float32) / 255.0)
        non_green = np.any(output != GREEN, axis=2)
        remaining_gold = (
            non_green
            & (output_hue >= 35.0)
            & (output_hue <= 55.0)
            & (output_saturation > 0.50)
        )
        gold_count = int(remaining_gold.sum())
        passed &= gold_count == 0
        print(f"{'':20} gold armour pixels remaining: {gold_count}   {'PASS' if gold_count == 0 else 'FAIL'}")
        print(f"{'':20} flat background: {'PASS' if background_ok else 'FAIL'}")

    print("\nRESULT: " + ("PASS" if passed else "FAIL"))
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
