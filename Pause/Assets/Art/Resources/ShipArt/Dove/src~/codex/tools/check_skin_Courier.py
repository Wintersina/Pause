#!/usr/bin/env python3
"""Audit Courier sheets against the approved 3x3 references."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)
CELL = 1024


def canopy_region(shape: tuple[int, int, int]) -> np.ndarray:
    h, w, _ = shape
    points = [
        (round(0.50 * w), round(0.12 * h)),
        (round(0.61 * w), round(0.22 * h)),
        (round(0.62 * w), round(0.33 * h)),
        (round(0.54 * w), round(0.44 * h)),
        (round(0.50 * w), round(0.46 * h)),
        (round(0.46 * w), round(0.44 * h)),
        (round(0.38 * w), round(0.33 * h)),
        (round(0.39 * w), round(0.22 * h)),
    ]
    mask = Image.new("1", (w, h), 0)
    ImageDraw.Draw(mask).polygon(points, fill=1)
    return np.asarray(mask, dtype=bool)


def pct(value: float) -> str:
    return f"{value * 100:.3f}%"


def has_pink_or_magenta(rgb: np.ndarray) -> bool:
    # Broad enough to catch purple/pink contamination while allowing the
    # approved red lights and orange damage (which have a low blue channel).
    p = rgb.astype(np.int16)
    r, g, b = p[..., 0], p[..., 1], p[..., 2]
    magenta = (
        (r + b >= 4)
        & (r > g * 1.35)
        & (b > r * 0.55)
        & (b < r * 1.8)
        & ((np.maximum.reduce([r, g, b]) - np.minimum.reduce([r, g, b])) > np.maximum.reduce([r, g, b]) * 0.38)
        & (np.maximum(r, b) < 180)
    )
    return bool(np.any(magenta))


def main() -> int:
    failures: list[str] = []
    changed_values: list[float] = []
    for state in range(3):
        ref_path = ROOT / f"skinref_state{state}.png"
        out_path = ROOT / f"skin_Courier_state{state}.png"
        if not out_path.exists():
            failures.append(f"state{state}: missing {out_path.name}")
            continue
        ref_native = Image.open(ref_path).convert("RGB")
        out_image = Image.open(out_path).convert("RGB")
        ref = np.asarray(ref_native.resize(out_image.size, Image.Resampling.NEAREST))
        out = np.asarray(out_image)
        if out.shape != (3072, 3072, 3):
            failures.append(f"state{state}: wrong output size {out_image.size}, expected (3072, 3072)")
            continue
        if has_pink_or_magenta(out):
            failures.append(f"state{state}: pink/magenta pixel present")

        print(f"state{state} {out_path.name}")
        for cell_index in range(9):
            row, col = divmod(cell_index, 3)
            y0, y1 = row * CELL, (row + 1) * CELL
            x0, x1 = col * CELL, (col + 1) * CELL
            rcell = ref[y0:y1, x0:x1]
            ocell = out[y0:y1, x0:x1]
            rmask = np.any(rcell != GREEN, axis=-1)
            omask = np.any(ocell != GREEN, axis=-1)
            silhouette_diff = np.logical_xor(rmask, omask).mean()
            canopy = canopy_region(rcell.shape) & rmask
            canopy_diff = np.logical_and(np.any(rcell != ocell, axis=-1), canopy).sum() / max(int(canopy.sum()), 1)
            changed = np.any(rcell != ocell, axis=-1).mean()
            changed_values.append(changed)
            ok = silhouette_diff < 0.015 and canopy_diff < 0.06 and changed > 0.03
            status = "PASS" if ok else "FAIL"
            print(
                f"  cell {cell_index}: {status} | silhouette diff {pct(silhouette_diff)} "
                f"(need < 1.500%) | canopy diff {pct(canopy_diff)} "
                f"(need < 6.000%) | changed {pct(changed)} (need > 3.000%)"
            )
            if not ok:
                failures.append(
                    f"state{state} cell{cell_index}: silhouette={pct(silhouette_diff)}, "
                    f"canopy={pct(canopy_diff)}, changed={pct(changed)}"
                )

    if changed_values:
        print(
            f"summary: cells=27, changed range={pct(min(changed_values))}..{pct(max(changed_values))}, "
            "thresholds silhouette<1.500%, canopy<6.000%, changed>3.000%"
        )
    if failures:
        print("FAILURES:")
        for failure in failures:
            print(f"  {failure}")
        return 1
    print("summary: ALL 27 CELLS PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
