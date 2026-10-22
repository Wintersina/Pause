#!/usr/bin/env python3
"""Audit Night sheets against the approved 3x3 reference sheets."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)
CELL = 1024


def canopy_mask(cell: np.ndarray) -> np.ndarray:
    """Find the canopy/glass region without depending on the Night palette."""
    p = cell.astype(np.int16)
    r, g, b = p[..., 0], p[..., 1], p[..., 2]
    non_green = np.any(p != GREEN.astype(np.int16), axis=-1)
    blue_glass = non_green & (b > g * 1.18) & (b > r * 1.25) & (g > 20)
    if int(blue_glass.sum()) >= 32:
        return blue_glass
    # The hit-flash canopy is bone-white, so use the fixed central upper window area.
    h, w = cell.shape[:2]
    region = np.zeros((h, w), dtype=bool)
    region[int(h * 0.08):int(h * 0.48), int(w * 0.29):int(w * 0.71)] = True
    return region & non_green


def pct(value: float) -> str:
    return f"{value * 100:.3f}%"


def main() -> int:
    failures: list[str] = []
    all_changed: list[float] = []
    for state in range(3):
        ref_path = ROOT / f"skinref_state{state}.png"
        out_path = ROOT / f"skin_Night_state{state}.png"
        ref_native = Image.open(ref_path).convert("RGB")
        out_im = Image.open(out_path).convert("RGB")
        ref_im = ref_native.resize(out_im.size, Image.Resampling.NEAREST)
        ref = np.asarray(ref_im)
        out = np.asarray(out_im)
        if out.shape != (3072, 3072, 3):
            failures.append(f"state{state}: wrong output size {out_im.size}, expected (3072, 3072)")
            continue
        if np.any(np.all(out == np.array([255, 0, 255], dtype=np.uint8), axis=-1)):
            failures.append(f"state{state}: exact magenta pixel present")
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
            canopy = canopy_mask(rcell)
            canopy_diff = np.logical_and(np.any(rcell != ocell, axis=-1), canopy).sum() / max(int(canopy.sum()), 1)
            changed = np.any(rcell != ocell, axis=-1).mean()
            all_changed.append(changed)
            ok = silhouette_diff < 0.015 and canopy_diff < 0.06 and changed > 0.12
            status = "PASS" if ok else "FAIL"
            print(
                f"  cell {cell_index}: {status} | silhouette diff {pct(silhouette_diff)} "
                f"(need < 1.500%) | canopy diff {pct(canopy_diff)} "
                f"(need < 6.000%) | changed {pct(changed)} (need > 12.000%)"
            )
            if not ok:
                failures.append(
                    f"state{state} cell{cell_index}: silhouette={pct(silhouette_diff)}, "
                    f"canopy={pct(canopy_diff)}, changed={pct(changed)}"
                )
    if all_changed:
        print(
            f"summary: cells=27, changed range={pct(min(all_changed))}..{pct(max(all_changed))}, "
            f"thresholds silhouette<1.500%, canopy<6.000%, changed>12.000%"
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
