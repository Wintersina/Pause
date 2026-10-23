#!/usr/bin/env python3
"""Print and enforce the J1b pose-sheet contract."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
GREEN = np.array([0, 255, 0], dtype=np.uint8)
SIZE = 1024
THRESHOLD = 60


def load(path: Path) -> np.ndarray:
    image = Image.open(path).convert("RGB")
    if image.size != (SIZE * 3, SIZE * 2):
        raise AssertionError(f"{path.name}: expected 3072x2048, got {image.size}")
    return np.asarray(image, dtype=np.uint8)


def cells(sheet: np.ndarray) -> list[np.ndarray]:
    return [
        sheet[row * SIZE:(row + 1) * SIZE, col * SIZE:(col + 1) * SIZE]
        for row in range(2)
        for col in range(3)
    ]


def diff(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    return np.abs(a.astype(np.int16) - b.astype(np.int16)).sum(axis=2) > THRESHOLD


def pct(mask: np.ndarray) -> float:
    return 100.0 * float(mask.mean())


def pct_over(mask: np.ndarray, denominator: np.ndarray) -> float:
    return 100.0 * float(mask.sum()) / max(1, int(denominator.sum()))


def ship_mask(image: np.ndarray) -> np.ndarray:
    return np.any(image != GREEN, axis=2)


def bbox_width(image: np.ndarray) -> int:
    mask = ship_mask(image)
    y, x = np.where(mask)
    return int(x.max() - x.min() + 1) if len(x) else 0


def canopy_mask(image: np.ndarray) -> np.ndarray:
    # The approved canopy glass region, restricted to the central glass polygon.
    mask = Image.new("1", (SIZE, SIZE), 0)
    from PIL import ImageDraw
    ImageDraw.Draw(mask).polygon(
        [(493, 238), (544, 245), (567, 326), (559, 425), (535, 472),
         (487, 472), (460, 420), (447, 325)],
        fill=1,
    )
    return np.asarray(mask, dtype=bool)


def best_translation_residual(key: np.ndarray, bank: np.ndarray) -> tuple[float, tuple[int, int]]:
    bank_ship = ship_mask(bank)

    def score(dx: int, dy: int, step: int) -> float:
        ys = np.arange(max(0, -dy), min(SIZE, SIZE - dy), step)
        xs = np.arange(max(0, -dx), min(SIZE, SIZE - dx), step)
        a = key[np.ix_(ys, xs)]
        b = bank[np.ix_(ys + dy, xs + dx)]
        changed = np.abs(a.astype(np.int16) - b.astype(np.int16)).sum(axis=2) > THRESHOLD
        overlap = int(changed.sum()) * step * step
        bank_overlap = int(bank_ship[np.ix_(ys + dy, xs + dx)].sum()) * step * step
        outside = int(bank_ship.sum()) - bank_overlap
        return 100.0 * (overlap + outside) / (SIZE * SIZE)

    # Coarse scan keeps the checker quick; the final score is exact at full
    # resolution around the best integer candidate.
    coarse = [(score(dx, dy, 4), dx, dy) for dy in range(-24, 25) for dx in range(-24, 25)]
    _, coarse_dx, coarse_dy = min(coarse)
    exact = []
    for dy in range(max(-24, coarse_dy - 3), min(24, coarse_dy + 3) + 1):
        for dx in range(max(-24, coarse_dx - 3), min(24, coarse_dx + 3) + 1):
            exact.append((score(dx, dy, 1), dx, dy))
    best, dx, dy = min(exact)
    return best, (dx, dy)


def row(name: str, key: np.ndarray, sheet: np.ndarray) -> list[bool]:
    pose = cells(sheet)
    key_mask = ship_mask(key)
    key_canopy = canopy_mask(key)
    values = [pct(diff(key, pose[i])) for i in range(5)]
    canopy_values = [pct_over(diff(key, pose[i]) & key_canopy, key_canopy) for i in (1, 2)]
    left_residual, left_shift = best_translation_residual(key, pose[3])
    right_residual, right_shift = best_translation_residual(key, pose[4])
    key_width = bbox_width(key)
    left_ratio = bbox_width(pose[3]) / key_width
    right_ratio = bbox_width(pose[4]) / key_width
    left_sil = pct(key_mask ^ ship_mask(pose[3]))
    right_sil = pct(key_mask ^ ship_mask(pose[4]))
    lr_diff = pct(diff(pose[3], pose[4]))
    print(
        f"{name:18} cells0-4 " + " ".join(f"{v:6.3f}%" for v in values)
    )
    print(
        f"{'':18} canopy c1/c2 {canopy_values[0]:6.3f}%/{canopy_values[1]:6.3f}% | "
        f"bank residual L/R {left_residual:6.3f}%/{right_residual:6.3f}% "
        f"shift {left_shift}/{right_shift}"
    )
    print(
        f"{'':18} silhouette L/R {left_sil:6.3f}%/{right_sil:6.3f}% | "
        f"bbox ratio L/R {left_ratio:6.3f}/{right_ratio:6.3f} | L-v-R {lr_diff:6.3f}%"
    )
    checks = [
        values[0] >= 0.4,
        values[1] >= 0.8,
        values[2] > values[1],
        values[3] >= 5.0,
        values[4] >= 5.0,
        canopy_values[0] > 0.0,
        canopy_values[1] > 0.0,
        3.0 <= left_sil <= 25.0,
        3.0 <= right_sil <= 25.0,
        0.80 <= left_ratio <= 1.02,
        0.80 <= right_ratio <= 1.02,
        lr_diff >= 4.0,
    ]
    return checks


def main() -> int:
    all_checks: list[bool] = []
    print("J1b validation (pixel-diff threshold: summed RGB > 60)")
    for state in range(3):
        key = np.asarray(Image.open(ROOT / f"key_state{state}.png").convert("RGB"), dtype=np.uint8)
        if key.shape != (SIZE, SIZE, 3):
            raise AssertionError(f"key_state{state}.png: expected 1024x1024, got {key.shape[:2]}")
        sheet = load(ROOT / f"hull_redo_state{state}.png")
        checks = row(f"state{state}", key, sheet)
        all_checks.extend(checks)
    distinct = []
    for state in range(3):
        pose = cells(load(ROOT / f"hull_redo_state{state}.png"))
        distinct.append(pct(diff(pose[3], pose[4])))
    print("bank left/right distinct: " + " ".join(f"state{i}={v:.3f}%" for i, v in enumerate(distinct)))
    passed = all(all_checks)
    print("RESULT:", "PASS" if passed else "FAIL")
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
