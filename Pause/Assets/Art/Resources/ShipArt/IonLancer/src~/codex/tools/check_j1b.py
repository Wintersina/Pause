from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.int16)
THRESHOLD = 60


def load(path):
    return np.asarray(Image.open(path).convert("RGB"), dtype=np.int16)


def cell(sheet, index):
    x = (index % 3) * 1024
    y = (index // 3) * 1024
    return sheet[y : y + 1024, x : x + 1024]


def non_green(a):
    d = np.abs(a - GREEN).sum(axis=2)
    return d > 30


def changed_percent(a, b):
    d = np.abs(a - b).sum(axis=2)
    return float((d > THRESHOLD).mean() * 100.0)


def canopy_region():
    yy, xx = np.mgrid[0:1024, 0:1024]
    return (((xx - 512.0) / 58.0) ** 2 + ((yy - 337.0) / 132.0) ** 2) <= 1.0


def best_translation_residual(bank, key):
    best = None
    # Search a small integer window around the common approved anchor.
    for dy in range(-24, 25):
        for dx in range(-24, 25):
            shifted = np.full_like(key, GREEN)
            sx0, sx1 = max(0, -dx), min(1024, 1024 - dx)
            sy0, sy1 = max(0, -dy), min(1024, 1024 - dy)
            tx0, tx1 = sx0 + dx, sx1 + dx
            ty0, ty1 = sy0 + dy, sy1 + dy
            shifted[ty0:ty1, tx0:tx1] = key[sy0:sy1, sx0:sx1]
            diff = np.abs(bank - shifted).sum(axis=2) > THRESHOLD
            score = int(diff.sum())
            if best is None or score < best[0]:
                best = score, dx, dy, shifted
    return best


def silhouette_difference(bank, shifted_key):
    lhs = non_green(bank)
    rhs = non_green(shifted_key)
    return float(np.logical_xor(lhs, rhs).mean() * 100.0)


def main():
    for state in range(3):
        key = load(ROOT / f"key_state{state}.png")
        sheet = load(ROOT / f"hull_redo_state{state}.png")
        print(f"state{state}")
        print("  cell   diff_vs_key_%")
        diffs = {}
        for idx in range(5):
            diffs[idx] = changed_percent(cell(sheet, idx), key)
            print(f"    {idx}      {diffs[idx]:7.3f}")
        cm = canopy_region()
        for idx in (1, 2):
            a = cell(sheet, idx)
            d = np.abs(a - key).sum(axis=2) > THRESHOLD
            print(f"  canopy cell{idx}_diff_% {float(d[cm].mean() * 100.0):7.3f}")
        bank_results = {}
        for idx in (3, 4):
            result = best_translation_residual(cell(sheet, idx), key)
            bank_results[idx] = result
            residual, dx, dy, shifted = result
            print(
                f"  bank{idx - 2} residual_% {residual / (1024 * 1024) * 100.0:7.3f} "
                f"(best_shift={dx:+d},{dy:+d}) silhouette_diff_% "
                f"{silhouette_difference(cell(sheet, idx), shifted):7.3f}"
            )
        if diffs[0] < 0.4:
            raise SystemExit(f"FAIL state{state} cell0 below 0.4%")
        if diffs[1] < 0.8:
            raise SystemExit(f"FAIL state{state} cell1 below 0.8%")
        if np.array_equal(cell(sheet, 1), cell(sheet, 2)):
            raise SystemExit(f"FAIL state{state} cell2 is identical to cell1")
        for idx in (3, 4):
            residual, _, _, shifted = bank_results[idx]
            if residual / (1024 * 1024) * 100.0 < 5.0:
                raise SystemExit(f"FAIL state{state} bank{idx - 2} residual below 5%")
            if silhouette_difference(cell(sheet, idx), shifted) < 3.0:
                raise SystemExit(f"FAIL state{state} bank{idx - 2} silhouette below 3%")


if __name__ == "__main__":
    main()
