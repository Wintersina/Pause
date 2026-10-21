#!/usr/bin/env python3
"""Validate J1b pose-sheet cell deltas and bank-pose separation."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.int16)
FILES = [ROOT / f"hull_redo_state{i}.png" for i in range(3)]


def pct(count: int, total: int) -> float:
    return 100.0 * count / total if total else 0.0


def threshold_delta(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    return np.abs(a.astype(np.int16) - b.astype(np.int16)).sum(axis=2) > 60


def mask(image: np.ndarray) -> np.ndarray:
    return np.any(image != GREEN, axis=2)


def canopy_mask(key: np.ndarray) -> np.ndarray:
    r, g, b = [key[:, :, i].astype(np.int16) for i in range(3)]
    region = np.zeros(key.shape[:2], dtype=bool)
    region[220:480, 430:600] = True
    return region & (b > r + 20) & (g > r + 12) & (b > 70)


def best_translation_residual(key: np.ndarray, bank: np.ndarray) -> tuple[float, tuple[int, int]]:
    """Find the best integer translation without allocating 4,225 full frames.

    A 4x nearest-neighbour preview finds the basin; a full-resolution 9x9 search
    around that basin reports the final integer residual.
    """
    h, w = key.shape[:2]

    def score(dx: int, dy: int, step: int = 1) -> float:
        if step == 1:
            ka, ba = key, bank
            ox, oy = dx, dy
        else:
            ka, ba = key[::step, ::step], bank[::step, ::step]
            ox, oy = round(dx / step), round(dy / step)
        shifted = np.full_like(ka, GREEN)
        hh, ww = ka.shape[:2]
        sy0 = max(0, -oy)
        sy1 = min(hh, hh - oy)
        sx0 = max(0, -ox)
        sx1 = min(ww, ww - ox)
        shifted[sy0:sy1, sx0:sx1] = ka[sy0 + oy : sy1 + oy, sx0 + ox : sx1 + ox]
        return pct(int(threshold_delta(ba, shifted).sum()), ba.shape[0] * ba.shape[1])

    coarse = (100.0, 0, 0)
    for dy in range(-128, 129, 8):
        for dx in range(-128, 129, 8):
            value = score(dx, dy, step=4)
            if value < coarse[0]:
                coarse = (value, dx, dy)

    best = (100.0, coarse[1], coarse[2])
    for dy in range(coarse[2] - 4, coarse[2] + 5):
        for dx in range(coarse[1] - 4, coarse[1] + 5):
            value = score(dx, dy)
            if value < best[0]:
                best = (value, dx, dy)
    return best[0], (best[1], best[2])


def row_for_file(path: Path, image: np.ndarray, key: np.ndarray) -> tuple[list[str], list[str]]:
    errors: list[str] = []
    lines: list[str] = []
    if image.shape != (2048, 3072, 3):
        errors.append(f"{path.name}: expected 3072x2048 RGB, got {image.shape}")
        return lines, errors

    key_canopy = canopy_mask(key)
    key_flat = key.reshape(-1, 3)
    for cell in range(5):
        x = (cell % 3) * 1024
        y = (cell // 3) * 1024
        current = image[y : y + 1024, x : x + 1024]
        delta = threshold_delta(current, key)
        value = pct(int(delta.sum()), 1024 * 1024)
        lines.append(f"{path.name} cell{cell} vs key: {value:.3f}%")
        if cell == 0 and value < 0.4:
            errors.append(f"{path.name} cell0 delta {value:.3f}% < 0.400%")
        if cell == 1 and value < 0.8:
            errors.append(f"{path.name} cell1 delta {value:.3f}% < 0.800%")
        if cell in (1, 2):
            canopy_current = current[key_canopy]
            canopy_key = key[key_canopy]
            canopy_delta = threshold_delta(canopy_current.reshape(-1, 1, 3), canopy_key.reshape(-1, 1, 3)).reshape(-1)
            canopy_value = pct(int(canopy_delta.sum()), int(key_canopy.sum()))
            lines.append(f"{path.name} cell{cell} canopy delta: {canopy_value:.3f}%")
        if cell in (3, 4):
            bank_mask = mask(current)
            key_mask = mask(key)
            silhouette = pct(int(np.logical_xor(bank_mask, key_mask).sum()), 1024 * 1024)
            residual, shift = best_translation_residual(key, current)
            lines.append(f"{path.name} cell{cell} bank residual after best translation {shift}: {residual:.3f}%")
            lines.append(f"{path.name} cell{cell} silhouette delta: {silhouette:.3f}%")
            if residual < 5.0:
                errors.append(f"{path.name} cell{cell} residual {residual:.3f}% < 5.000%")
            if silhouette < 3.0:
                errors.append(f"{path.name} cell{cell} silhouette delta {silhouette:.3f}% < 3.000%")

    c1 = image[0:1024, 1024:2048]
    c2 = image[0:1024, 2048:3072]
    if not threshold_delta(c1, c2).any():
        errors.append(f"{path.name}: cell2 does not differ from cell1")
    if not np.all(image[1024:2048, 2048:3072] == GREEN):
        errors.append(f"{path.name}: cell5 is not empty green")
    return lines, errors


def main() -> int:
    images = [np.asarray(Image.open(path).convert("RGB")) for path in FILES]
    keys = [np.asarray(Image.open(ROOT / f"key_state{i}.png").convert("RGB")) for i in range(3)]
    errors: list[str] = []
    print("J1b validation")
    for path, image, key in zip(FILES, images, keys):
        lines, file_errors = row_for_file(path, image, key)
        print("\n".join(lines))
        errors.extend(file_errors)
    if errors:
        print("FAIL")
        for error in errors:
            print(f"- {error}")
        return 1
    print("PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
