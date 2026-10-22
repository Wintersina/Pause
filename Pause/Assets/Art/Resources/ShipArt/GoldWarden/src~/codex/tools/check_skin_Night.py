#!/usr/bin/env python3
"""Audit Night sheets against the approved 3x3 reference flipbooks."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)
REFERENCES = tuple(ROOT / f"skinref_state{i}.png" for i in range(3))
OUTPUTS = tuple(ROOT / f"skin_Night_state{i}.png" for i in range(3))


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


def load_reference(path: Path) -> np.ndarray:
    with Image.open(path) as image:
        array = np.asarray(image.convert("RGB"), dtype=np.uint8)
    if array.shape != (1536, 1536, 3):
        raise ValueError(f"{path.name}: expected 1536x1536 RGB")
    return array


def load_output(path: Path) -> np.ndarray:
    with Image.open(path) as image:
        array = np.asarray(image.convert("RGB"), dtype=np.uint8)
    if array.shape != (3072, 3072, 3):
        raise ValueError(f"{path.name}: expected 3072x3072 RGB")
    # The deliverable is deliberately 2x nearest-neighbour; this is the exact native-pixel view.
    return array[::2, ::2]


def percent(count: int, total: int) -> float:
    return 100.0 * count / total if total else 0.0


def cell(array: np.ndarray, index: int) -> np.ndarray:
    row, column = divmod(index, 3)
    return array[row * 512:(row + 1) * 512, column * 512:(column + 1) * 512]


def canopy_mask(reference: np.ndarray) -> np.ndarray:
    hue, saturation, value = hsv(reference.astype(np.float32) / 255.0)
    blue_region = (((hue >= 150.0) & (hue <= 270.0)) | ((hue >= 170.0) & (hue <= 230.0)))
    return blue_region & (saturation > 0.16) & (value > 0.08)


def main() -> int:
    passed = True
    print("file                 cell  silhouette Δ   canopy/blue Δ   changed pixels   requirements")
    print("-------------------- ----- --------------- --------------- ---------------  ---------------------------")
    for reference_path, output_path in zip(REFERENCES, OUTPUTS):
        reference = load_reference(reference_path)
        output = load_output(output_path)
        reference_green = np.all(reference == GREEN, axis=2)
        if not np.all(output[reference_green] == GREEN):
            raise AssertionError(f"{output_path.name}: background is not flat pure #00FF00")
        for index in range(9):
            ref_cell = cell(reference, index)
            out_cell = cell(output, index)
            ref_non_green = np.any(ref_cell != GREEN, axis=2)
            out_non_green = np.any(out_cell != GREEN, axis=2)
            silhouette_delta = percent(int(np.count_nonzero(ref_non_green != out_non_green)), int(ref_non_green.sum()))
            blue = canopy_mask(ref_cell)
            canopy_delta = percent(int(np.count_nonzero(np.any(ref_cell[blue] != out_cell[blue], axis=1))), int(blue.sum()))
            changed = percent(int(np.count_nonzero(np.any(ref_cell != out_cell, axis=2))), ref_cell.shape[0] * ref_cell.shape[1])
            # The approved hit-flash cell is intentionally unchanged bone-white.
            changed_ok = changed > 12.0 if index != 8 else changed == 0.0
            ok = silhouette_delta < 1.5 and canopy_delta < 6.0 and changed_ok
            passed &= ok
            requirement = ">12.0%" if index != 8 else "0.0% approved flash"
            print(f"{output_path.name:<20} {index:>4}  {silhouette_delta:>12.3f}%  {canopy_delta:>12.3f}%  {changed:>12.3f}%   {'PASS' if ok else 'FAIL'} ({requirement})")

        hue, saturation, _ = hsv(reference.astype(np.float32) / 255.0)
        armor_gold = (hue >= 35.0) & (hue <= 55.0) & (saturation > 0.5) & np.any(reference != GREEN, axis=2)
        # The hit-flash is bone-white and cannot satisfy the saturation test, so no exception is needed.
        output_hue, output_saturation, _ = hsv(output.astype(np.float32) / 255.0)
        remaining_gold = armor_gold & (output_hue >= 35.0) & (output_hue <= 55.0) & (output_saturation > 0.5)
        gold_count = int(remaining_gold.sum())
        passed &= gold_count == 0
        print(f"{'':20} gold armour pixels remaining: {gold_count}   {'PASS' if gold_count == 0 else 'FAIL'}")

    print("\nRESULT: " + ("PASS" if passed else "FAIL"))
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
