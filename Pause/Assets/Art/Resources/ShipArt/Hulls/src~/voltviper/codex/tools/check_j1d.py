#!/usr/bin/env python3
"""Audit the J1d bank sprites and print the requested acceptance table."""
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)


def load(name: str) -> np.ndarray:
    return np.asarray(Image.open(ROOT / name).convert("RGB"))


def silhouette(a: np.ndarray) -> np.ndarray:
    return np.any(a != GREEN, axis=2)


def greenish(a: np.ndarray) -> np.ndarray:
    r, g, b = [a[..., i].astype(np.int16) for i in range(3)]
    return (g > 180) & (r < 80) & (b < 80) & (g - r > 80) & (g - b > 80)


def bbox(m: np.ndarray) -> tuple[int, int, int, int]:
    ys, xs = np.where(m)
    return (int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())) if len(xs) else (0, 0, 0, 0)


def shifted(m: np.ndarray, dx: int, dy: int) -> np.ndarray:
    out = np.zeros_like(m)
    h, w = m.shape
    sx0, sx1 = max(0, -dx), min(w, w - dx)
    sy0, sy1 = max(0, -dy), min(h, h - dy)
    tx0, tx1 = sx0 + dx, sx1 + dx
    ty0, ty1 = sy0 + dy, sy1 + dy
    if sx0 < sx1 and sy0 < sy1:
        out[ty0:ty1, tx0:tx1] = m[sy0:sy1, sx0:sx1]
    return out


def best_key_difference(key: np.ndarray, bank: np.ndarray) -> tuple[float, tuple[int, int]]:
    key_area = int(key.sum())
    best = (float("inf"), (0, 0))
    for dy in range(-96, 97):
        for dx in range(-96, 97):
            b = shifted(bank, dx, dy)
            diff = np.count_nonzero(key ^ b) / key_area
            if diff < best[0]:
                best = (float(diff), (dx, dy))
    return best


def shape_difference(a: np.ndarray, b: np.ndarray) -> float:
    union = np.count_nonzero(a | b)
    return np.count_nonzero(a ^ b) / union if union else 0.0


def ratio_lr(m: np.ndarray) -> float:
    left = int(m[:, :512].sum())
    right = int(m[:, 512:].sum())
    return left / right if right else float("inf")


def main() -> None:
    key = silhouette(load("key_state0.png"))
    files = [f"bank_state{s}_{side}.png" for s in range(3) for side in ("left", "right")]
    masks = {f: silhouette(load(f)) for f in files}
    print("file                   size       bbox                 key-diff  lr-area  green-in-sil")
    print("---------------------- ---------- -------------------- --------- -------- ------------")
    for f in files:
        a = load(f)
        m = masks[f]
        diff, shift = best_key_difference(key, m)
        print(f"{f:22} {a.shape[1]}x{a.shape[0]:<5} {str(bbox(m)):20} {diff*100:7.2f}% {ratio_lr(m):8.3f} {int(greenish(a)[m].sum()):12d}  shift={shift}")

    left0, right0 = masks["bank_state0_left.png"], masks["bank_state0_right.png"]
    print(f"\nleft-vs-right silhouette: non-flipped {shape_difference(left0, right0)*100:.2f}%  mirrored {shape_difference(left0, np.fliplr(right0))*100:.2f}%")
    for side in ("left", "right"):
        base = masks[f"bank_state0_{side}.png"]
        for state in (1, 2):
            delta = np.count_nonzero(base ^ masks[f"bank_state{state}_{side}.png"]) / base.sum() * 100
            print(f"{side} state0-vs-state{state} silhouette difference: {delta:.3f}%")


if __name__ == "__main__":
    main()
