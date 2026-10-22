#!/usr/bin/env python3
"""Audit Bone sheets against the approved 3x3 references."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)


def blue_region(cell: np.ndarray) -> np.ndarray:
    r, g, b = [cell[..., i].astype(np.int16) for i in range(3)]
    return (
        (g > 50)
        & (b > 50)
        & (b > r * 1.10)
        & (g > r * 1.10)
    )


def audit(state: int) -> list[tuple[float, float, float]]:
    ref = np.asarray(Image.open(ROOT / f"skinref_state{state}.png").convert("RGB"))
    out = np.asarray(Image.open(ROOT / f"skin_Bone_state{state}.png").convert("RGB"))
    if ref.shape != (1536, 1536, 3):
        raise ValueError(f"bad reference dimensions for state {state}: {ref.shape}")
    if out.shape != (3072, 3072, 3):
        raise ValueError(f"bad output dimensions for state {state}: {out.shape}")

    expected = np.asarray(
        Image.fromarray(ref, mode="RGB").resize((3072, 3072), Image.Resampling.NEAREST)
    )
    rows = []
    for cy in range(3):
        for cx in range(3):
            y0, y1 = cy * 1024, (cy + 1) * 1024
            x0, x1 = cx * 1024, (cx + 1) * 1024
            got = out[y0:y1, x0:x1]
            exp = expected[y0:y1, x0:x1]
            ref_cell = ref[cy * 512 : (cy + 1) * 512, cx * 512 : (cx + 1) * 512]

            got_mask = np.any(got != GREEN, axis=2)
            exp_mask = np.any(exp != GREEN, axis=2)
            silhouette_diff = np.logical_xor(got_mask, exp_mask).mean() * 100.0

            blue = blue_region(np.asarray(
                Image.fromarray(ref_cell, mode="RGB").resize((1024, 1024), Image.Resampling.NEAREST)
            ))
            canopy_diff = (
                np.any(got != exp, axis=2)[blue].mean() * 100.0 if blue.any() else 0.0
            )
            changed = np.any(got != exp, axis=2).mean() * 100.0
            rows.append((silhouette_diff, canopy_diff, changed))
            print(
                f"state{state} cell{cy * 3 + cx}: "
                f"silhouette={silhouette_diff:.3f}% "
                f"canopy={canopy_diff:.3f}% "
                f"changed={changed:.3f}%"
            )
    return rows


def main() -> None:
    all_rows = []
    for state in range(3):
        all_rows.extend(audit(state))

    silhouette_max = max(row[0] for row in all_rows)
    canopy_max = max(row[1] for row in all_rows)
    changed_min = min(row[2] for row in all_rows)
    passed = silhouette_max < 1.5 and canopy_max < 6.0 and changed_min > 20.0
    print(
        "summary: "
        f"silhouette_max={silhouette_max:.3f}% "
        f"canopy_max={canopy_max:.3f}% "
        f"changed_min={changed_min:.3f}% "
        f"PASS={passed}"
    )
    raise SystemExit(0 if passed else 1)


if __name__ == "__main__":
    main()
