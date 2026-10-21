#!/usr/bin/env python3
"""Audit Kaneda sheets against the approved 3x3 reference flipbook."""

from __future__ import annotations

import colorsys
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
CELL = 512
OUT_CELL = 1024
REF_SIZE = 1536
SHEET = 3072
GREEN = (0, 255, 0)
REFS = [ROOT / f"skinref_state{i}.png" for i in range(3)]
OUTS = [ROOT / f"skin_Kaneda_state{i}.png" for i in range(3)]


def non_green(rgb: tuple[int, int, int]) -> bool:
    return rgb != GREEN


def blue_region(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    h, sat, value = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
    return value > 0.12 and sat > 0.12 and (b > r * 1.12 or (g > r * 1.12 and b > g * 0.65))


def cell(image: Image.Image, row: int, col: int, size: int) -> Image.Image:
    return image.crop((col * size, row * size, (col + 1) * size, (row + 1) * size))


def audit(ref: Image.Image, out: Image.Image) -> tuple[float, float, float]:
    ref = ref.resize((CELL, CELL), Image.Resampling.NEAREST)
    out = out.resize((CELL, CELL), Image.Resampling.NEAREST)
    rp = list(ref.get_flattened_data())
    op = list(out.get_flattened_data())
    silhouette_diff = sum(non_green(a) != non_green(b) for a, b in zip(rp, op)) / len(rp) * 100
    blue = [i for i, rgb in enumerate(rp) if blue_region(rgb)]
    canopy_diff = (sum(rp[i] != op[i] for i in blue) / len(blue) * 100) if blue else 0.0
    changed = sum(a != b for a, b in zip(rp, op)) / len(rp) * 100
    return silhouette_diff, canopy_diff, changed


def main() -> int:
    all_pass = True
    print("skin_Kaneda audit (per cell, 0-based row/column)")
    print("file                  cell  silhouette_diff  canopy_diff  pixels_changed  result")
    for ref_path, out_path in zip(REFS, OUTS):
        if not out_path.exists():
            print(f"{out_path.name:23} MISSING")
            all_pass = False
            continue
        ref_sheet = Image.open(ref_path).convert("RGB")
        out_sheet = Image.open(out_path).convert("RGB")
        if ref_sheet.size != (REF_SIZE, REF_SIZE) or out_sheet.size != (SHEET, SHEET):
            print(f"{out_path.name:23} INVALID SIZE ref={ref_sheet.size} out={out_sheet.size}")
            all_pass = False
            continue
        for row in range(3):
            for col in range(3):
                metrics = audit(cell(ref_sheet, row, col, CELL), cell(out_sheet, row, col, OUT_CELL))
                ok = metrics[0] < 1.5 and metrics[1] < 6.0 and metrics[2] > 3.0
                all_pass &= ok
                print(
                    f"{out_path.name:23} {row},{col}   "
                    f"{metrics[0]:7.3f}%         {metrics[1]:7.3f}%     "
                    f"{metrics[2]:7.3f}%       {'PASS' if ok else 'FAIL'}"
                )
    print("thresholds: silhouette < 1.5%, canopy/blue < 6%, changed > 3%")
    return 0 if all_pass else 1


if __name__ == "__main__":
    raise SystemExit(main())
