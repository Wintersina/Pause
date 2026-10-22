#!/usr/bin/env python3
"""Validate Night sheets against their approved 3x3 references."""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)


def load(path: Path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGB"))


def non_green(rgb: np.ndarray) -> np.ndarray:
    # Robust to the reference's tiny green-edge quantisation noise; the output
    # background itself is required to be exactly #00FF00.
    r, g, b = rgb.astype(np.int16).transpose(2, 0, 1)
    return ~((g > 180) & (g > r * 1.6) & (g > b * 1.6))


def teal_core(rgb: np.ndarray) -> np.ndarray:
    r, g, b = rgb.astype(np.int16).transpose(2, 0, 1)
    return (b > 100) & (g > 80) & (b > r * 1.35) & (g > r * 1.15) & ~((g > 180) & (g > r * 1.6) & (g > b * 1.6))


def pct(value: float) -> str:
    return f"{value * 100:.3f}%"


def check_state(state: int) -> tuple[float, float, float]:
    ref = load(ROOT / f"skinref_state{state}.png")
    out_big = load(ROOT / f"skin_Night_state{state}.png")
    if out_big.shape != (3072, 3072, 3):
        raise SystemExit(f"state{state}: expected 3072x3072 RGB, got {out_big.shape}")
    if not np.all(out_big[0, 0] == GREEN):
        raise SystemExit(f"state{state}: background is not flat #00FF00")
    out = out_big[::2, ::2]

    silhouette_values = []
    core_values = []
    changed_values = []
    for row in range(3):
        for col in range(3):
            y0, y1 = row * 512, (row + 1) * 512
            x0, x1 = col * 512, (col + 1) * 512
            rr = ref[y0:y1, x0:x1]
            oo = out[y0:y1, x0:x1]
            ref_mask = non_green(rr)
            out_mask = non_green(oo)
            core = teal_core(rr)
            silhouette = float(np.mean(ref_mask != out_mask))
            core_diff = float(np.mean(np.any(rr[core] != oo[core], axis=1))) if np.any(core) else 0.0
            changed = float(np.mean(np.any(rr != oo, axis=2)))
            silhouette_values.append(silhouette)
            core_values.append(core_diff)
            changed_values.append(changed)
            ok = silhouette < 0.015 and core_diff < 0.06 and changed > 0.12
            status = "PASS" if ok else "FAIL"
            print(f"state{state} cell {row * 3 + col}: silhouette={pct(silhouette)} core={pct(core_diff)} changed={pct(changed)} {status}")
            if not ok:
                raise SystemExit(f"state{state} cell {row * 3 + col} failed threshold")
    return max(silhouette_values), max(core_values), min(changed_values)


if __name__ == "__main__":
    summaries = []
    for state in range(3):
        summaries.append((state, *check_state(state)))
    print("summary:")
    for state, silhouette, core, changed in summaries:
        print(f"state{state}: max_silhouette={pct(silhouette)} max_core={pct(core)} min_changed={pct(changed)} PASS")
