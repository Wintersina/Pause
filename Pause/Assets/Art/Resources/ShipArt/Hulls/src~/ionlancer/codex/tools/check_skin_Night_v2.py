"""Audit the new Night state1/state2 v2 sheets against approved references."""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)


def read(path: Path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGB"), dtype=np.uint8)


def cells(sheet: np.ndarray, size: int):
    return [
        sheet[y : y + size, x : x + size]
        for y in range(0, sheet.shape[0], size)
        for x in range(0, sheet.shape[1], size)
    ]


def glass_mask(rgb: np.ndarray) -> np.ndarray:
    r, g, b = (rgb[..., i].astype(np.float32) for i in range(3))
    mx = rgb.max(axis=2).astype(np.float32)
    mn = rgb.min(axis=2).astype(np.float32)
    delta = mx - mn
    hue = np.zeros_like(mx)
    nz = delta > 0
    rr, gg, bb, mm, dd = r[nz], g[nz], b[nz], mx[nz], delta[nz]
    hue[nz] = np.where(
        mm == rr,
        (gg - bb) / dd,
        np.where(mm == gg, 2 + (bb - rr) / dd, 4 + (rr - gg) / dd),
    ) * 60.0
    hue %= 360.0
    sat = np.divide(delta, mx, out=np.zeros_like(delta), where=mx > 0)
    return (
        (hue >= 160)
        & (hue <= 220)
        & (sat > 0.34)
        & (g > 16)
        & ((g / np.maximum(b, 1)) > 0.54)
        & ((g / np.maximum(b, 1)) < 1.16)
    )


def compare(reference: np.ndarray, output: np.ndarray):
    reduced = output[::2, ::2]
    ref_mask = np.any(reference != GREEN, axis=2)
    out_mask = np.any(reduced != GREEN, axis=2)
    silhouette = np.mean(ref_mask != out_mask) * 100.0
    glass = glass_mask(reference)
    glass_diff = (
        np.mean(np.any(reference[glass] != reduced[glass], axis=1)) * 100.0
        if np.any(glass)
        else 0.0
    )
    changed = np.mean(np.any(reference != reduced, axis=2)) * 100.0
    return silhouette, glass_diff, changed


def main() -> None:
    all_ok = True
    for state in (1, 2):
        reference = read(ROOT / f"skinref_state{state}.png")
        output_path = ROOT / f"skin_Night_state{state}_v2.png"
        output = read(output_path)
        if output.shape != (3072, 3072, 3):
            raise SystemExit(f"{output_path.name}: expected 3072x3072 RGB, got {output.shape}")
        for cell, (ref_cell, out_cell) in enumerate(zip(cells(reference, 512), cells(output, 1024))):
            sil, glass, changed = compare(ref_cell, out_cell)
            ok = sil < 1.5 and glass < 6.0 and changed > 12.0
            all_ok &= ok
            print(
                f"{output_path.name} cell {cell}: "
                f"silhouette={sil:.3f}% "
                f"canopy_blue_glass={glass:.3f}% "
                f"changed={changed:.3f}% "
                f"{'PASS' if ok else 'FAIL'}"
            )
    raise SystemExit(0 if all_ok else 1)


if __name__ == "__main__":
    main()
