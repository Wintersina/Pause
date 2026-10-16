#!/usr/bin/env python3
"""Verify the Tide gust sheet's geometry, palette, seams, and cell coverage."""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
PATH = ROOT / "wx_gust.png"
CELL = 512


def bbox(alpha: np.ndarray, threshold: int = 8):
    ys, xs = np.where(alpha > threshold)
    return None if not len(xs) else (int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1))


def block_repeat_score(cell: np.ndarray, n: int) -> float:
    """Score exact n x n repeated blocks, ignoring fully transparent space."""
    rgba = cell[:-n + 1, :-n + 1]
    same = np.ones(rgba.shape[:2], dtype=bool)
    for dy in range(n):
        for dx in range(n):
            same &= np.all(cell[dy : dy + rgba.shape[0], dx : dx + rgba.shape[1]] == rgba, axis=2)
    active = np.any(rgba[..., 3] > 8, axis=0) if rgba.ndim == 3 else None
    # Use cells whose top-left pixel is visible; this avoids transparent canvas
    # dominating the score while keeping the test conservative.
    active = rgba[..., 3] > 8
    return float(same[active].mean()) if active.any() else 0.0


def main() -> None:
    image = Image.open(PATH).convert("RGBA")
    assert image.size == (2048, 1024), image.size
    data = np.asarray(image)
    assert data.shape == (1024, 2048, 4)

    allowed = {
        (5, 6, 12), (18, 52, 58), (32, 91, 91), (83, 145, 137),
        (143, 208, 196), (124, 242, 192), (240, 255, 250),
        (0, 0, 0),
    }
    visible = data[..., 3] > 0
    colours = {tuple(c) for c in data[..., :3][visible]}
    unexpected = colours - allowed
    assert not unexpected, sorted(unexpected)[:8]

    # Explicit red / pink guard, independent of the exact-palette assertion.
    rgb = data[..., :3].astype(np.int16)
    redish = visible & (rgb[..., 0] > rgb[..., 1] + 20) & (rgb[..., 0] > rgb[..., 2] + 20)
    pinkish = visible & (rgb[..., 0] > 120) & (rgb[..., 2] > 90) & (rgb[..., 1] < rgb[..., 0] - 15)
    assert not redish.any(), "red pixels found"
    assert not pinkish.any(), "pink pixels found"

    print("size: PASS 2048x1024 RGBA")
    print(f"palette: PASS {len(colours)} exact colours; no red/pink")
    for row in range(2):
        for col in range(4):
            cell = data[row * CELL : (row + 1) * CELL, col * CELL : (col + 1) * CELL]
            print(f"cell {row},{col}: bbox>8={bbox(cell[...,3])}")

    means = []
    for col in range(4):
        cell = data[:CELL, col * CELL : (col + 1) * CELL]
        means.append(float(cell[..., 3].mean() / 255.0))
        edge_diff = np.abs(cell[:, 0, 3].astype(np.int16) - cell[:, -1, 3].astype(np.int16))
        print(f"ribbon {col}: mean alpha={means[-1]:.3f}, mean edge diff={edge_diff.mean()/255:.4f}, max edge diff={edge_diff.max()}")
        assert 0.35 <= means[-1] <= 0.45
        assert edge_diff.mean() <= 12
    print("ribbon alpha/seams: PASS")

    for col in range(2):
        cell = data[CELL:, col * CELL : (col + 1) * CELL]
        assert np.count_nonzero(cell[..., 3] > 8) > 10000
    for col in (2, 3):
        cell = data[CELL:, col * CELL : (col + 1) * CELL]
        bb = bbox(cell[..., 3], 32)
        assert bb and max(bb[2] - bb[0], bb[3] - bb[1]) <= 120
    print("used cells/debris scale: PASS")

    scores = {n: max(block_repeat_score(data, n), block_repeat_score(data[:, :1024], n)) for n in (2, 3, 4)}
    print("exact repeated n×n block scores:", ", ".join(f"{n}x{n}={score:.3f}" for n, score in scores.items()))
    assert max(scores.values()) < 0.65, "dominant block-upscale signature found"
    print("pixel-scale check: PASS (no dominant 2x/3x/4x block upscale)")


if __name__ == "__main__":
    main()
