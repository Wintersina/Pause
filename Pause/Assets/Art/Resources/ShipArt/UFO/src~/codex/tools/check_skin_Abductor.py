"""Audit Abductor UFO sheets against the approved 1536px references."""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)


def hsv(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    c = rgb.astype(np.float32) / 255.0
    mx = c.max(axis=-1)
    mn = c.min(axis=-1)
    delta = mx - mn
    r, g, b = c[..., 0], c[..., 1], c[..., 2]
    hue = np.zeros_like(mx)
    valid = delta > 1e-6
    sel = valid & (mx == r)
    hue[sel] = ((g[sel] - b[sel]) / delta[sel]) % 6.0
    sel = valid & (mx == g)
    hue[sel] = (b[sel] - r[sel]) / delta[sel] + 2.0
    sel = valid & (mx == b)
    hue[sel] = (r[sel] - g[sel]) / delta[sel] + 4.0
    return hue * 60.0, np.divide(delta, mx, out=np.zeros_like(delta), where=mx > 0), mx


def cell(sheet: np.ndarray, row: int, col: int, size: int) -> np.ndarray:
    return sheet[row * size:(row + 1) * size, col * size:(col + 1) * size]


def metrics(ref: np.ndarray, out: np.ndarray) -> tuple[float, float, float]:
    ref_mask = ~np.all(ref == GREEN, axis=-1)
    out_mask = ~np.all(out == GREEN, axis=-1)
    silhouette = 100.0 * np.count_nonzero(ref_mask != out_mask) / ref_mask.size

    h, s, v = hsv(ref)
    canopy = ref_mask & (h >= 160.0) & (h <= 230.0) & (s > 0.22) & (v > 0.18)
    if np.count_nonzero(canopy):
        canopy_diff = 100.0 * np.count_nonzero(np.any(ref[canopy] != out[canopy], axis=-1)) / np.count_nonzero(canopy)
    else:
        canopy_diff = 0.0

    changed = 100.0 * np.count_nonzero(np.any(ref != out, axis=-1)) / ref_mask.size
    return float(silhouette), float(canopy_diff), float(changed)


def main() -> int:
    all_ok = True
    for state in range(3):
        ref_sheet = np.asarray(Image.open(ROOT / f"skinref_state{state}.png").convert("RGB"), dtype=np.uint8)
        out_path = ROOT / f"skin_Abductor_state{state}.png"
        out_sheet = np.asarray(Image.open(out_path).convert("RGB"), dtype=np.uint8)
        if out_sheet.shape != (3072, 3072, 3):
            raise SystemExit(f"state{state}: expected 3072x3072 RGB, got {out_sheet.shape}")

        print(f"skin_Abductor_state{state}.png")
        for row in range(3):
            for col in range(3):
                ref = cell(ref_sheet, row, col, 512)
                # Exact nearest-neighbour reduction recovers the native painted cell.
                out = cell(out_sheet, row, col, 1024)[::2, ::2]
                silhouette, canopy, changed = metrics(ref, out)
                exempt = row == 2 and col == 2
                ok = silhouette < 1.5 and canopy < 6.0 and (exempt or changed > 3.0)
                all_ok &= ok
                tag = "PASS" if ok else "FAIL"
                flash = " (hit-flash)" if exempt else ""
                print(
                    f"  cell {row * 3 + col}: silhouette={silhouette:.3f}% "
                    f"canopy_blue={canopy:.3f}% changed={changed:.3f}%{flash} {tag}"
                )

    return 0 if all_ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
