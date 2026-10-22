#!/usr/bin/env python3
"""Audit the Amber sheets against the approved keyed references."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)


def hsv(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    f = rgb.astype(np.float32) / 255.0
    r, g, b = f[..., 0], f[..., 1], f[..., 2]
    mx = f.max(axis=-1)
    mn = f.min(axis=-1)
    d = mx - mn
    h = np.zeros_like(mx)
    nz = d > 1e-6
    rr = ((g - b) / np.maximum(d, 1e-6)) % 6.0
    gg = ((b - r) / np.maximum(d, 1e-6)) + 2.0
    bb = ((r - g) / np.maximum(d, 1e-6)) + 4.0
    h[nz & (mx == r)] = rr[nz & (mx == r)]
    h[nz & (mx == g)] = gg[nz & (mx == g)]
    h[nz & (mx == b)] = bb[nz & (mx == b)]
    h /= 6.0
    s = np.divide(d, np.maximum(mx, 1e-6), out=np.zeros_like(mx), where=mx > 1e-6)
    return h, s, mx


def cell(arr: np.ndarray, index: int, size: int) -> np.ndarray:
    y0 = (index // 3) * size
    x0 = (index % 3) * size
    return arr[y0:y0 + size, x0:x0 + size]


def metrics(ref: np.ndarray, out: np.ndarray) -> tuple[float, float, float]:
    ref_mask = np.any(ref != GREEN, axis=2)
    out_mask = np.any(out != GREEN, axis=2)
    silhouette = float(np.logical_xor(ref_mask, out_mask).mean() * 100.0)

    rh, rs, rv = hsv(ref)
    blue = ref_mask & (rs > 0.18) & (rh > 0.43) & (rh < 0.70) & (rv > 0.12)
    canopy_difference = 0.0 if not blue.any() else float(np.any(ref != out, axis=2)[blue].mean() * 100.0)
    changed = float(np.any(ref != out, axis=2)[ref_mask].mean() * 100.0)
    return silhouette, canopy_difference, changed


def main() -> None:
    failures: list[str] = []
    print("file                  cell  silhouette-diff  canopy/blue-diff  changed(non-green)")
    for state in range(3):
        ref_arr = np.asarray(Image.open(ROOT / f"skinref_state{state}.png").convert("RGB"))
        out_arr = np.asarray(Image.open(ROOT / f"skin_Amber_state{state}.png").convert("RGB"))
        if ref_arr.shape != (1536, 1536, 3):
            raise AssertionError(f"reference state{state} is not 1536x1536")
        if out_arr.shape != (3072, 3072, 3):
            raise AssertionError(f"skin_Amber_state{state} is not 3072x3072")

        # The final export must be an exact 2x2 nearest-neighbour block for
        # every source pixel.
        reduced = out_arr.reshape(1536, 2, 1536, 2, 3)[:, 0, :, 0, :]
        if not np.array_equal(out_arr[:, 0::2, :], out_arr[:, 1::2, :]):
            failures.append(f"state{state}: horizontal upscale is not nearest-neighbour")
        if not np.array_equal(out_arr[0::2, :, :], out_arr[1::2, :, :]):
            failures.append(f"state{state}: vertical upscale is not nearest-neighbour")

        for index in range(9):
            ref = cell(ref_arr, index, 512)
            got = cell(reduced, index, 512)
            sd, cd, changed = metrics(ref, got)
            print(f"skin_Amber_state{state}.png {index:>4}  {sd:>15.3f}%  {cd:>16.3f}%  {changed:>17.3f}%")
            if sd >= 1.5:
                failures.append(f"state{state} cell{index}: silhouette {sd:.3f}%")
            if cd >= 6.0:
                failures.append(f"state{state} cell{index}: canopy {cd:.3f}%")
            if changed <= 20.0:
                failures.append(f"state{state} cell{index}: changed {changed:.3f}%")

        bg = np.all(out_arr == GREEN, axis=2)
        if not np.all(out_arr[bg] == GREEN):
            failures.append(f"state{state}: keyed background changed")
        h, s, _ = hsv(out_arr)
        r = out_arr[..., 0].astype(np.float32) / 255.0
        b = out_arr[..., 2].astype(np.float32) / 255.0
        ship = ~bg
        magenta = ship & (s > 0.50) & (h > 0.83) & (h < 0.97) & (r > 0.25) & (b > 0.25)
        green_ship = ship & (h > 0.24) & (h < 0.42) & (s > 0.35) & (_ > 0.25)
        if magenta.any():
            failures.append(f"state{state}: saturated magenta/pink pixels present")
        if green_ship.any():
            failures.append(f"state{state}: green pixels on ship present")

    if failures:
        print("\nFAIL")
        for failure in failures:
            print(f"- {failure}")
        raise SystemExit(1)
    print("\nAmber validation: PASS (all 27 cells meet the requested thresholds)")


if __name__ == "__main__":
    main()
