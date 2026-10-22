from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.int16)


def diff_mask(a: np.ndarray, b: np.ndarray, threshold: int = 60) -> np.ndarray:
    return np.abs(a.astype(np.int16) - b.astype(np.int16)).sum(axis=2) > threshold


def canopy_mask(shape: tuple[int, int]) -> np.ndarray:
    m = Image.new("1", (shape[1], shape[0]), 0)
    ImageDraw.Draw(m).polygon([(511, 237), (587, 296), (574, 420), (512, 443), (446, 405), (443, 296)], fill=1)
    return np.asarray(m, dtype=bool)


def non_green(a: np.ndarray) -> np.ndarray:
    return np.any(a != GREEN, axis=2)


def best_translation_residual(cell: np.ndarray, key: np.ndarray) -> float:
    best = 100.0
    h, w = key.shape[:2]
    for dy in range(-24, 25):
        for dx in range(-24, 25):
            shifted = np.full_like(key, GREEN)
            y0, y1 = max(0, dy), min(h, h + dy)
            x0, x1 = max(0, dx), min(w, w + dx)
            sy0, sy1 = max(0, -dy), min(h, h - dy)
            sx0, sx1 = max(0, -dx), min(w, w - dx)
            shifted[y0:y1, x0:x1] = key[sy0:sy1, sx0:sx1]
            best = min(best, diff_mask(cell, shifted).mean() * 100.0)
    return best


def silhouette_difference(cell: np.ndarray, key: np.ndarray) -> float:
    a = non_green(cell)
    b = non_green(key)
    return np.logical_xor(a, b).sum() / max(1, b.sum()) * 100.0


def pct(v: float) -> str:
    return f"{v:6.2f}%"


def main() -> None:
    print("file                 cell0    cell1    cell2    cell3    cell4   canopy1 canopy2  bank3 residual bank4 residual  bank3 silhouette bank4 silhouette")
    for state in range(3):
        key = np.asarray(Image.open(ROOT / f"key_state{state}.png").convert("RGB"))
        sheet = np.asarray(Image.open(ROOT / f"hull_redo_state{state}.png").convert("RGB"))
        cells = [sheet[(i // 3) * 1024:(i // 3 + 1) * 1024, (i % 3) * 1024:(i % 3 + 1) * 1024] for i in range(6)]
        d = [diff_mask(c, key).mean() * 100.0 for c in cells[:5]]
        cm = canopy_mask(key.shape)
        c1 = diff_mask(cells[1], key)[cm].mean() * 100.0
        c2 = diff_mask(cells[2], key)[cm].mean() * 100.0
        r3 = best_translation_residual(cells[3], key)
        r4 = best_translation_residual(cells[4], key)
        s3 = silhouette_difference(cells[3], key)
        s4 = silhouette_difference(cells[4], key)
        print(f"state{state}              {pct(d[0])} {pct(d[1])} {pct(d[2])} {pct(d[3])} {pct(d[4])} {pct(c1)} {pct(c2)} {pct(r3)}       {pct(r4)}       {pct(s3)}           {pct(s4)}")

        assert d[0] >= 0.4, f"state{state} cell 0 below 0.4%"
        assert d[1] >= 0.8, f"state{state} cell 1 below 0.8%"
        assert np.any(diff_mask(cells[2], cells[1])), f"state{state} cell 2 did not differ from cell 1"
        assert r3 >= 5.0 and r4 >= 5.0, f"state{state} bank residual below 5%"
        assert s3 >= 3.0 and s4 >= 3.0, f"state{state} silhouette difference below 3%"
        assert np.all(cells[5] == GREEN), f"state{state} cell 5 is not empty green"


if __name__ == "__main__":
    main()
