#!/usr/bin/env python3
"""Validate Sodium sheets against the approved 512px-cell references."""

from __future__ import annotations

import colorsys
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)


def load(path: Path) -> np.ndarray:
    return np.array(Image.open(path).convert("RGB"), dtype=np.uint8)


def mask(rgb: np.ndarray) -> np.ndarray:
    return np.any(rgb != GREEN, axis=2)


def hsv_image(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    flat = rgb.reshape(-1, 3).astype(np.float32) / 255.0
    hsv = np.array([colorsys.rgb_to_hsv(*pixel) for pixel in flat], dtype=np.float32)
    hsv = hsv.reshape(rgb.shape)
    return hsv[..., 0] * 360.0, hsv[..., 1], hsv[..., 2]


def teal_region(cell: np.ndarray) -> np.ndarray:
    hue, sat, value = hsv_image(cell)
    yy, xx = np.indices(cell.shape[:2])
    central = (xx >= 155) & (xx < 357) & (yy >= 62) & (yy < 250)
    return central & (hue >= 150.0) & (hue < 204.0) & (sat >= 0.16) & (value > 0.10)


def downsample_output(sheet: np.ndarray, row: int, col: int) -> np.ndarray:
    cell = sheet[row * 1024 : (row + 1) * 1024, col * 1024 : (col + 1) * 1024]
    return cell[::2, ::2]


def percent(value: float) -> str:
    return f"{100.0 * value:7.3f}%"


def main() -> int:
    ok = True
    print("file                   cell  silhouette diff  canopy diff  pixels changed  status")
    print("----------------------  ----  ----------------  -----------  --------------  ------")
    for state in range(3):
        reference = load(ROOT / f"skinref_state{state}.png")
        output_path = ROOT / f"skin_Sodium_state{state}.png"
        if not output_path.exists():
            print(f"missing {output_path.name}")
            ok = False
            continue
        output = load(output_path)
        if output.shape != (3072, 3072, 3):
            print(f"{output_path.name} has invalid size {output.shape[1]}x{output.shape[0]}")
            ok = False
            continue
        for row in range(3):
            for col in range(3):
                ref_cell = reference[row * 512 : (row + 1) * 512, col * 512 : (col + 1) * 512]
                out_cell = downsample_output(output, row, col)
                ref_mask = mask(ref_cell)
                out_mask = mask(out_cell)
                silhouette_diff = float(np.mean(ref_mask != out_mask))

                canopy = teal_region(ref_cell)
                canopy_diff = float(np.mean(np.any(out_cell[canopy] != ref_cell[canopy], axis=1))) if np.any(canopy) else 0.0
                changed = float(np.mean(np.any(out_cell != ref_cell, axis=2)))
                cell_ok = silhouette_diff < 0.015 and canopy_diff < 0.06 and changed > 0.12
                ok &= cell_ok
                print(
                    f"{output_path.name:<22}  {row * 3 + col + 1:>4}  "
                    f"{percent(silhouette_diff):>16}  {percent(canopy_diff):>11}  "
                    f"{percent(changed):>14}  {'PASS' if cell_ok else 'FAIL'}"
                )
    print("\nSKIN-Sodium CHECK: " + ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
