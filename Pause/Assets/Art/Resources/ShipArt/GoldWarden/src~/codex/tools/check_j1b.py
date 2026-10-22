#!/usr/bin/env python3
"""Validate the GoldWarden J1b pose sheets and print the requested audit table."""

from pathlib import Path

from PIL import Image
import numpy as np


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)
SHEET_NAMES = [f"hull_redo_state{i}.png" for i in range(3)]
KEY_NAMES = [f"key_state{i}.png" for i in range(3)]


def load(path: Path) -> np.ndarray:
    with Image.open(path) as image:
        image = image.convert("RGB")
        return np.array(image)


def cells(sheet: np.ndarray) -> list[np.ndarray]:
    if sheet.shape != (2048, 3072, 3):
        raise ValueError(f"expected 3072x2048 sheet, got {sheet.shape[1]}x{sheet.shape[0]}")
    return [sheet[y * 1024:(y + 1) * 1024, x * 1024:(x + 1) * 1024]
            for y in range(2) for x in range(3)]


def non_green(image: np.ndarray) -> np.ndarray:
    return np.any(image != GREEN, axis=2)


def diff60(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    return np.abs(a.astype(np.int16) - b.astype(np.int16)).sum(axis=2) > 60


def pct(value: int, total: int) -> float:
    return 100.0 * value / total if total else 0.0


def canopy_mask(key: np.ndarray) -> np.ndarray:
    yy, xx = np.indices((1024, 1024))
    polygon = ((xx - 512) ** 2 / 54 ** 2 + (yy - 300) ** 2 / 128 ** 2) <= 1.0
    r, g, b = [key[:, :, i].astype(np.int16) for i in range(3)]
    return polygon & (b > r + 25) & (g > r + 15) & (b > 100)


def shifted(image: np.ndarray, dx: int, dy: int) -> np.ndarray:
    result = np.full_like(image, GREEN)
    src_x0, src_x1 = max(0, -dx), min(1024, 1024 - dx)
    src_y0, src_y1 = max(0, -dy), min(1024, 1024 - dy)
    dst_x0, dst_x1 = max(0, dx), min(1024, 1024 + dx)
    dst_y0, dst_y1 = max(0, dy), min(1024, 1024 + dy)
    result[dst_y0:dst_y1, dst_x0:dst_x1] = image[src_y0:src_y1, src_x0:src_x1]
    return result


def best_translation_residual(key: np.ndarray, bank: np.ndarray) -> tuple[float, int, int]:
    bank_mask = non_green(bank)
    def center(mask: np.ndarray) -> tuple[int, int]:
        ys, xs = np.where(mask)
        return int(round(float(xs.mean()))), int(round(float(ys.mean())))

    key_cx, key_cy = center(non_green(key))
    bank_cx, bank_cy = center(bank_mask)
    estimate_x, estimate_y = bank_cx - key_cx, bank_cy - key_cy
    best = (float("inf"), 0, 0)
    # The candidates are authored in the same cell. Search the integer neighborhood
    # around the centroid alignment, which is the useful translation range here.
    for dy in range(estimate_y - 6, estimate_y + 7):
        for dx in range(estimate_x - 6, estimate_x + 7):
            moved = shifted(key, dx, dy)
            union = non_green(moved) | bank_mask
            residual = diff60(moved, bank)
            value = pct(int(np.count_nonzero(residual & union)), int(union.sum()))
            if value < best[0]:
                best = (value, dx, dy)
    return best


def silhouette_delta(a: np.ndarray, b: np.ndarray) -> float:
    am, bm = non_green(a), non_green(b)
    return pct(int(np.count_nonzero(am ^ bm)), int(am.sum()))


def wing_widths(image: np.ndarray, row: int = 560) -> tuple[int, int]:
    mask = non_green(image)[row]
    xs = np.where(mask)[0]
    left = xs[xs < 512]
    right = xs[xs > 512]
    return (int(512 - left.min()) if len(left) else 0,
            int(right.max() - 512) if len(right) else 0)


def main() -> int:
    keys = [load(ROOT / name) for name in KEY_NAMES]
    sheets = [cells(load(ROOT / name)) for name in SHEET_NAMES]
    passed = True

    print("J1b pixel-difference audit (threshold: summed RGB > 60)")
    print("file/state  cell0    cell1    cell2    cell3    cell4    canopy1 canopy2")
    print("----------  -------  -------  -------  -------  -------  -------  -------")
    rows = []
    for index, (key, sheet) in enumerate(zip(keys, sheets)):
        values = [pct(int(diff60(key, sheet[cell]).sum()), 1024 * 1024) for cell in range(5)]
        canopy = canopy_mask(key)
        canopy1 = pct(int((diff60(key, sheet[1]) & canopy).sum()), int(canopy.sum()))
        canopy2 = pct(int((diff60(key, sheet[2]) & canopy).sum()), int(canopy.sum()))
        rows.append(values + [canopy1, canopy2])
        print(f"state{index:<5}  " + "  ".join(f"{value:7.3f}%" for value in rows[-1]))
        passed &= values[0] >= 0.4 and values[1] >= 0.8
        passed &= values[2] > 0.0 and canopy1 > 0.0 and canopy2 > 0.0

    print("\nBank residual after best integer translation")
    print("state/side  residual   dx   dy   silhouette-v-key")
    print("----------  --------  ---  ---  ----------------")
    residuals = {}
    for index, (key, sheet) in enumerate(zip(keys, sheets)):
        for cell, label in ((3, "left"), (4, "right")):
            residual, dx, dy = best_translation_residual(key, sheet[cell])
            silhouette = silhouette_delta(key, sheet[cell])
            residuals[(index, cell)] = residual
            print(f"state{index}/{label:<5}  {residual:7.3f}%  {dx:3d}  {dy:3d}  {silhouette:14.3f}%")
            passed &= residual >= 5.0 and 3.0 <= silhouette <= 22.0

    print("\nBank handedness and wing widths (row 560)")
    print("state       mirror-silhouette  left wing  right wing  low-v-high  high<=key")
    print("----------  ------------------  ---------  ----------  ----------  ---------")
    for index, (key, sheet) in enumerate(zip(keys, sheets)):
        mirror_right = np.fliplr(sheet[4])
        mirror_delta = silhouette_delta(sheet[3], mirror_right)
        left_l, left_r = wing_widths(sheet[3])
        right_l, right_r = wing_widths(sheet[4])
        left_narrow = 100.0 * (1.0 - left_l / left_r) if left_r else 0.0
        right_narrow = 100.0 * (1.0 - right_r / right_l) if right_l else 0.0
        key_l, key_r = wing_widths(key)
        low_high = f"L {left_narrow:5.1f}% / R {right_narrow:5.1f}%"
        high_ok = f"L {left_r <= key_r} / R {right_l <= key_l}"
        print(f"state{index:<5}  {mirror_delta:16.3f}%  {left_l:9d}  {left_r:10d}  {low_high:>10}  {high_ok}")
        passed &= mirror_delta >= 3.0
        passed &= 10.0 <= left_narrow <= 30.0
        passed &= 10.0 <= right_narrow <= 30.0
        passed &= left_r <= key_r and right_l <= key_l

    print("\nOther checks")
    for index, sheet in enumerate(sheets):
        empty_ok = not non_green(sheet[5]).any()
        cell2_differs = diff60(sheet[1], sheet[2]).sum() > 0
        print(f"state{index}: cell5 empty={empty_ok} cell2-differs-from-cell1={cell2_differs}")
        passed &= empty_ok and cell2_differs

    print("\nRESULT: " + ("PASS" if passed else "FAIL"))
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
