#!/usr/bin/env python3
"""Validate the requested J1b Ninja pose-sheet metrics."""

from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
GREEN = np.array([0, 255, 0], dtype=np.int16)
SHEET_SIZE = (3072, 2048)
CELL_SIZE = 1024


def load(path: Path) -> np.ndarray:
    image = Image.open(path).convert("RGB")
    if image.size != SHEET_SIZE:
        raise AssertionError(f"{path.name}: expected 3072x2048, got {image.size}")
    return np.asarray(image, dtype=np.int16)


def cell(sheet: np.ndarray, index: int) -> np.ndarray:
    x = (index % 3) * CELL_SIZE
    y = (index // 3) * CELL_SIZE
    return sheet[y : y + CELL_SIZE, x : x + CELL_SIZE]


def fg(image: np.ndarray) -> np.ndarray:
    return np.any(image != GREEN, axis=2)


def gem_mask() -> np.ndarray:
    yy, xx = np.mgrid[0:CELL_SIZE, 0:CELL_SIZE]
    return (np.abs(xx - 512) / 70.0 + np.abs(yy - 512) / 78.0 <= 1.0)


def threshold_diff(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    return np.sum(np.abs(a - b), axis=2) > 60


def pct(value: int | np.ndarray, denominator: int | np.ndarray) -> float:
    return 100.0 * float(np.asarray(value).sum()) / max(1, int(np.asarray(denominator).sum()))


def shifted_overlap(a: np.ndarray, b: np.ndarray, dx: int, dy: int) -> tuple[np.ndarray, np.ndarray]:
    """Return overlapping arrays after translating a by (dx, dy)."""
    height_total, width_total = a.shape[:2]
    ax0 = max(0, -dx)
    ay0 = max(0, -dy)
    bx0 = max(0, dx)
    by0 = max(0, dy)
    width = width_total - abs(dx)
    height = height_total - abs(dy)
    return a[ay0 : ay0 + height, ax0 : ax0 + width], b[by0 : by0 + height, bx0 : bx0 + width]


def best_translation_residual(key: np.ndarray, bank: np.ndarray) -> tuple[float, tuple[int, int]]:
    """Find the best integer translation using coarse-to-fine search."""
    # Coarse search is only a locator; the final metric is evaluated at full resolution.
    key_small = key[::4, ::4]
    bank_small = bank[::4, ::4]
    best = (1.0, (0, 0))
    for dy in range(-64, 65, 4):
        for dx in range(-64, 65, 4):
            aa, bb = shifted_overlap(key_small, bank_small, dx // 4, dy // 4)
            score = float(np.count_nonzero(threshold_diff(aa, bb))) / aa.shape[0] / aa.shape[1]
            if score < best[0]:
                best = (score, (dx, dy))
    coarse_dx, coarse_dy = best[1]
    for dy in range(coarse_dy - 4, coarse_dy + 5):
        for dx in range(coarse_dx - 4, coarse_dx + 5):
            aa, bb = shifted_overlap(key, bank, dx, dy)
            score = float(np.count_nonzero(threshold_diff(aa, bb))) / aa.shape[0] / aa.shape[1]
            if score < best[0]:
                best = (score, (dx, dy))
    return best


def silhouette_difference(a: np.ndarray, b: np.ndarray) -> float:
    ma = fg(a)
    mb = fg(b)
    return pct(np.count_nonzero(ma != mb), np.count_nonzero(ma))


def main() -> int:
    sheets = [load(ROOT / f"hull_redo_state{state}.png") for state in range(3)]
    keys = [np.asarray(Image.open(ROOT / f"key_state{state}.png").convert("RGB"), dtype=np.int16) for state in range(3)]
    gem = gem_mask()
    failed = False

    print("file                 cell  diff>60(all)   gem diff (1/2)   requirement")
    for state, (sheet, key) in enumerate(zip(sheets, keys)):
        name = f"hull_redo_state{state}.png"
        for index in range(5):
            diff_pct = pct(np.count_nonzero(threshold_diff(cell(sheet, index), key)), CELL_SIZE * CELL_SIZE)
            gem_pct = "-"
            if index in (1, 2):
                gem_pct = f"{pct(np.count_nonzero(threshold_diff(cell(sheet, index), key)[gem]), np.count_nonzero(gem)):7.3f}%"
            print(f"{name:<20} {index:>4} {diff_pct:12.3f}% {gem_pct:>15}   cell-specific")
            if index == 0:
                failed |= diff_pct < 0.4
            if index == 1:
                failed |= diff_pct < 0.8

        c1 = cell(sheet, 1)
        c2 = cell(sheet, 2)
        c1c2 = pct(np.count_nonzero(threshold_diff(c1, c2)), CELL_SIZE * CELL_SIZE)
        print(f"{'':20} c1->c2 {c1c2:9.3f}%   must be > 0")
        failed |= c1c2 <= 0.0

        left_residual, left_shift = best_translation_residual(key, cell(sheet, 3))
        right_residual, right_shift = best_translation_residual(key, cell(sheet, 4))
        left_sil = silhouette_difference(key, cell(sheet, 3))
        right_sil = silhouette_difference(key, cell(sheet, 4))
        bank_sil = silhouette_difference(cell(sheet, 3), cell(sheet, 4))
        print(f"{'':20} bank-left residual {100*left_residual:7.3f}% shift={left_shift!s:<10} >= 8.0%")
        print(f"{'':20} bank-right residual {100*right_residual:7.3f}% shift={right_shift!s:<10} >= 8.0%")
        print(f"{'':20} bank-left silhouette {left_sil:7.3f}%                 >= 6.0%")
        print(f"{'':20} bank-right silhouette {right_sil:7.3f}%                >= 6.0%")
        print(f"{'':20} left/right silhouette {bank_sil:7.3f}%               >= 6.0%")
        failed |= left_residual * 100 < 8.0 or right_residual * 100 < 8.0
        failed |= left_sil < 6.0 or right_sil < 6.0 or bank_sil < 6.0

    empty = [np.count_nonzero(fg(cell(sheet, 5))) for sheet in sheets]
    print(f"\ncell 5 non-green pixels: {empty} (must be [0, 0, 0])")
    failed |= any(empty)
    print("\nRESULT: " + ("FAIL" if failed else "PASS"))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
