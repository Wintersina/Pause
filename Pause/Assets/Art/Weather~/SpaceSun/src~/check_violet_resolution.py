#!/usr/bin/env python3
"""Reject aligned 2x, 3x or 4x block-upscaled violet 1024-cell art."""
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
SPECS = {"sun_violet.png": (2, 1), "sun_violet_corona.png": (2, 1),
         "flares_violet.png": (4, 2)}


def share(cell: np.ndarray, k: int) -> float:
    side = (1024 // k) * k
    a = cell[:side, :side]
    first = np.repeat(np.repeat(a[::k, ::k], k, axis=0), k, axis=1)
    opaque = a[..., 3] > 0
    return float(np.all(a == first, axis=2)[opaque].mean())


def main() -> None:
    failed = False
    for name, (cols, rows) in SPECS.items():
        pixels = np.asarray(Image.open(ROOT / name).convert("RGBA"))
        assert pixels.shape == (rows * 1024, cols * 1024, 4), (name, pixels.shape)
        for i in range(cols * rows):
            row, col = divmod(i, cols)
            cell = pixels[row * 1024:(row + 1) * 1024, col * 1024:(col + 1) * 1024]
            values = [share(cell, k) for k in (2, 3, 4)]
            failed |= any(v >= .97 for v in values)
            print(f"{name} cell {i}: k=2 {values[0]:.4f}, k=3 {values[1]:.4f}, k=4 {values[2]:.4f}")
    print("RESULT:", "FAIL" if failed else "PASS")
    raise SystemExit(1 if failed else 0)


if __name__ == "__main__":
    main()
