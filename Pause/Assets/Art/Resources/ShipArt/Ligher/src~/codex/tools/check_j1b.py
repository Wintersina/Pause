#!/usr/bin/env python3
"""Validate the J1b 3x2 Ligher pose sheets and print the audit table."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image


GREEN = np.array([0, 255, 0], dtype=np.uint8)
DIFF_THRESHOLD = 60
CELL = 1024


def load(path: Path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGB"))


def cells(sheet: np.ndarray) -> list[np.ndarray]:
    return [
        sheet[row * CELL : (row + 1) * CELL, col * CELL : (col + 1) * CELL]
        for row in range(2)
        for col in range(3)
    ]


def changed(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    return np.abs(a.astype(np.int16) - b.astype(np.int16)).sum(axis=2) > DIFF_THRESHOLD


def changed_percent(a: np.ndarray, b: np.ndarray, region: np.ndarray | None = None) -> float:
    m = changed(a, b)
    if region is not None:
        m = m[region]
    return float(m.mean() * 100.0)


def silhouette(a: np.ndarray) -> np.ndarray:
    return np.any(a != GREEN, axis=2)


def bbox_width(mask: np.ndarray) -> int:
    ys, xs = np.where(mask)
    return int(xs.max() - xs.min() + 1) if len(xs) else 0


def silhouette_delta(a: np.ndarray, b: np.ndarray) -> float:
    return float(np.logical_xor(silhouette(a), silhouette(b)).mean() * 100.0)


def canopy_mask(key: np.ndarray) -> np.ndarray:
    r, g, b = key[..., 0], key[..., 1], key[..., 2]
    roi = np.zeros(key.shape[:2], dtype=bool)
    roi[70:530, 410:615] = True
    return roi & (b > 55) & (g > 60) & (g > r * 1.08) & (b > r * 1.08)


def translate_key(key: np.ndarray, dx: int, dy: int) -> np.ndarray:
    out = np.empty_like(key)
    out[:] = GREEN
    sx0, sx1 = max(0, -dx), min(CELL, CELL - dx)
    sy0, sy1 = max(0, -dy), min(CELL, CELL - dy)
    dx0, dx1 = max(0, dx), min(CELL, CELL + dx)
    dy0, dy1 = max(0, dy), min(CELL, CELL + dy)
    if sx0 < sx1 and sy0 < sy1:
        out[dy0:dy1, dx0:dx1] = key[sy0:sy1, sx0:sx1]
    return out


def best_translation_residual(bank: np.ndarray, key: np.ndarray) -> tuple[float, tuple[int, int]]:
    best = 100.0
    best_shift = (0, 0)
    for dy in range(-16, 17):
        for dx in range(-16, 17):
            score = changed_percent(bank, translate_key(key, dx, dy))
            if score < best:
                best, best_shift = score, (dx, dy)
    return best, best_shift


def fmt(ok: bool, value: str) -> str:
    return f"{value} {'PASS' if ok else 'FAIL'}"


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    keys = [load(root / f"key_state{i}.png") for i in range(3)]
    sheets = [cells(load(root / f"hull_redo_state{i}.png")) for i in range(3)]

    print("J1b audit; RGB summed diff threshold = 60")
    print("file                 c0      c1      c2      c3      c4    canopy1 canopy2")
    reports: list[dict[str, float | bool]] = []
    for state, (key, sheet_cells) in enumerate(zip(keys, sheets)):
        diffs = [changed_percent(cell, key) for cell in sheet_cells[:5]]
        cm = canopy_mask(key)
        c1 = changed_percent(sheet_cells[1], key, cm)
        c2 = changed_percent(sheet_cells[2], key, cm)
        print(
            f"state{state}              "
            + " ".join(f"{v:7.3f}%" for v in diffs)
            + f" {c1:8.3f}% {c2:8.3f}%"
        )

        idle_mask = silhouette(key)
        left, right = sheet_cells[3], sheet_cells[4]
        left_res, left_shift = best_translation_residual(left, key)
        right_res, right_shift = best_translation_residual(right, key)
        left_sil = silhouette_delta(left, key)
        right_sil = silhouette_delta(right, key)
        idle_w = bbox_width(idle_mask)
        left_w = bbox_width(silhouette(left))
        right_w = bbox_width(silhouette(right))
        left_wc = abs(left_w - idle_w) / idle_w * 100.0
        right_wc = abs(right_w - idle_w) / idle_w * 100.0
        left_growth = (left_w - idle_w) / idle_w * 100.0
        right_growth = (right_w - idle_w) / idle_w * 100.0
        pair_sil = silhouette_delta(left, right)
        print(
            f"  banks: residual L {left_res:7.3f}% @ {left_shift}, R {right_res:7.3f}% @ {right_shift}; "
            f"sil L {left_sil:7.3f}%, R {right_sil:7.3f}%; "
            f"bbox L {left_w}px ({left_wc:5.2f}% change), R {right_w}px ({right_wc:5.2f}% change); "
            f"L-vs-R silhouette {pair_sil:7.3f}%"
        )
        reports.append(
            {
                "cell0": diffs[0] >= 0.4,
                "cell1": diffs[1] >= 0.8,
                "cell2_from_cell1": changed_percent(sheet_cells[2], sheet_cells[1]) >= 0.8,
                "canopy1": c1 > 0.0,
                "canopy2": c2 > 0.0,
                "left_res": left_res >= 5.0,
                "right_res": right_res >= 5.0,
                "left_sil": 3.0 <= left_sil <= 14.0,
                "right_sil": 3.0 <= right_sil <= 14.0,
                "left_width": 5.0 <= left_wc <= 25.0 and left_growth <= 6.0,
                "right_width": 5.0 <= right_wc <= 25.0 and right_growth <= 6.0,
                "pair": pair_sil >= 3.0,
            }
        )

    checks = [flag for report in reports for flag in report.values()]
    print(f"\nJ1b validation: {'PASS' if all(checks) else 'FAIL'}")
    if not all(checks):
        for state, report in enumerate(reports):
            failed = [name for name, ok in report.items() if not ok]
            if failed:
                print(f"state{state} failed: {', '.join(failed)}")
        raise SystemExit(1)


if __name__ == "__main__":
    main()
