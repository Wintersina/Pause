#!/usr/bin/env python3
"""Audit Neon GoldWarden sheets against the approved 3x3 references."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)


def hsv(rgb: np.ndarray) -> np.ndarray:
    return np.asarray(Image.fromarray(rgb, mode="RGB").convert("HSV"), dtype=np.uint8)


def symmetric_difference(a: np.ndarray, b: np.ndarray) -> float:
    union = np.logical_or(a, b).sum()
    if union == 0:
        return 0.0
    return float(np.logical_xor(a, b).sum() / union * 100.0)


def canopy_region(rgb: np.ndarray) -> np.ndarray:
    h = hsv(rgb)
    hue = h[..., 0].astype(np.float32) * 360.0 / 255.0
    sat = h[..., 1].astype(np.float32) / 255.0
    val = h[..., 2].astype(np.float32) / 255.0
    yy, xx = np.indices(rgb.shape[:2])
    cx, cy = xx % 512, yy % 512
    # The reference defines the blue/cyan glass envelope. The audit compares
    # occupancy inside this fixed region, so changing cyan glass to royal blue
    # does not falsely look like a geometry drift.
    return (
        (cy <= 245) & (cx >= 135) & (cx <= 380)
        & (hue >= 120) & (hue <= 275) & (sat > 0.16) & (val > 0.18)
        & np.any(rgb != GREEN, axis=2)
    )


def gold_armour_mask(ref: np.ndarray) -> np.ndarray:
    h = hsv(ref)
    hue = h[..., 0].astype(np.float32) * 360.0 / 255.0
    sat = h[..., 1].astype(np.float32) / 255.0
    val = h[..., 2].astype(np.float32) / 255.0
    yy, xx = np.indices(ref.shape[:2])
    cx, cy = xx % 512, yy % 512
    nozzle = (
        (((cx >= 130) & (cx <= 210)) | ((cx >= 302) & (cx <= 382)))
        & (cy >= 310) & (cy <= 430)
    )
    return (
        (hue >= 35) & (hue <= 55) & (sat > 0.5) & (val > 0.14)
        & np.any(ref != GREEN, axis=2) & ~nozzle
    )


def audit_one(state: int) -> list[bool]:
    ref = np.asarray(Image.open(ROOT / f"skinref_state{state}.png").convert("RGB"), dtype=np.uint8)
    out_path = ROOT / f"skin_Neon_state{state}.png"
    if not out_path.exists():
        print(f"state{state}: MISSING {out_path.name}")
        return [False]
    out_big = np.asarray(Image.open(out_path).convert("RGB"), dtype=np.uint8)
    shape_ok = out_big.shape[:2] == (3072, 3072)
    if not shape_ok:
        print(f"state{state}: FAIL size={out_big.shape[1]}x{out_big.shape[0]} expected=3072x3072")
        return [False]
    out = out_big[::2, ::2]
    ref_mask = np.any(ref != GREEN, axis=2)
    out_mask = np.any(out != GREEN, axis=2)
    ref_canopy = canopy_region(ref)
    out_canopy = ref_canopy & np.any(out != GREEN, axis=2)
    ref_gold = gold_armour_mask(ref)
    out_hsv = hsv(out)
    out_hue = out_hsv[..., 0].astype(np.float32) * 360.0 / 255.0
    out_sat = out_hsv[..., 1].astype(np.float32) / 255.0
    residual_gold = ref_gold & (out_hue >= 35) & (out_hue <= 55) & (out_sat > 0.5)
    results = []
    for row in range(3):
        for col in range(3):
            sl = (slice(row * 512, (row + 1) * 512), slice(col * 512, (col + 1) * 512))
            silhouette = symmetric_difference(ref_mask[sl], out_mask[sl])
            canopy = symmetric_difference(ref_canopy[sl], out_canopy[sl])
            changed = float(np.any(ref[sl] != out[sl], axis=2).mean() * 100.0)
            gold_left = int(residual_gold[sl].sum())
            ok = silhouette < 1.5 and canopy < 6.0 and changed > 12.0 and gold_left == 0
            results.append(ok)
            print(
                f"state{state} cell{row * 3 + col}: "
                f"silhouette={silhouette:.3f}% ( <1.5 ) "
                f"canopy={canopy:.3f}% ( <6 ) "
                f"changed={changed:.3f}% ( >12 ) "
                f"gold_armour_left={gold_left} "
                f"{'PASS' if ok else 'FAIL'}"
            )
    print(f"state{state}: {'PASS' if all(results) else 'FAIL'}")
    return results


def main() -> None:
    all_results = []
    for state in range(3):
        all_results.extend(audit_one(state))
    print(f"SUMMARY: {'PASS' if all(all_results) else 'FAIL'} ({sum(all_results)}/27 cells pass)")
    raise SystemExit(0 if all(all_results) else 1)


if __name__ == "__main__":
    main()
