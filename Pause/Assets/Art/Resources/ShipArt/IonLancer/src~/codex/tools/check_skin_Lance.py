"""Audit the Lance IonLancer sheets against the approved 3x3 flipbook."""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)


def read(path: Path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGB"), dtype=np.uint8)


def cells(sheet: np.ndarray, size: int) -> list[np.ndarray]:
    return [
        sheet[y : y + size, x : x + size]
        for y in range(0, sheet.shape[0], size)
        for x in range(0, sheet.shape[1], size)
    ]


def canopy_blue_glass_mask(rgb: np.ndarray) -> np.ndarray:
    a = rgb.astype(np.float32)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    maximum = a.max(axis=2)
    minimum = a.min(axis=2)
    delta = maximum - minimum
    hue = np.zeros_like(maximum)
    nz = delta > 0
    hue[nz] = np.where(
        maximum[nz] == r[nz], ((g[nz] - b[nz]) / delta[nz]) % 6,
        np.where(
            maximum[nz] == g[nz],
            (b[nz] - r[nz]) / delta[nz] + 2,
            (r[nz] - g[nz]) / delta[nz] + 4,
        ),
    ) * 60.0
    saturation = np.divide(delta, maximum, out=np.zeros_like(delta), where=maximum > 0)
    ratio = g / np.maximum(b, 1)
    return (
        (hue >= 165)
        & (hue <= 215)
        & (saturation > 0.45)
        & (g > 20)
        & (ratio > 0.58)
        & (ratio < 1.08)
    )


def compare(reference: np.ndarray, output: np.ndarray) -> tuple[float, float, float]:
    # The delivered sheet is a 2x nearest-neighbour export. Audit at the
    # approved 512px cell resolution so the deliberate export scale is ignored.
    reduced = output[::2, ::2]
    ref_mask = np.any(reference != GREEN, axis=2)
    out_mask = np.any(reduced != GREEN, axis=2)
    silhouette_diff = float(np.mean(ref_mask != out_mask) * 100.0)

    glass_mask = canopy_blue_glass_mask(reference)
    canopy_diff = (
        float(np.mean(np.any(reference[glass_mask] != reduced[glass_mask], axis=1)) * 100.0)
        if np.any(glass_mask)
        else 0.0
    )
    changed_fraction = float(np.mean(np.any(reference != reduced, axis=2)) * 100.0)
    return silhouette_diff, canopy_diff, changed_fraction


def main() -> None:
    all_ok = True
    for state in range(3):
        reference_path = ROOT / f"skinref_state{state}.png"
        output_path = ROOT / f"skin_Lance_state{state}.png"
        reference = read(reference_path)
        output = read(output_path)
        if reference.shape != (1536, 1536, 3):
            raise SystemExit(f"{reference_path.name}: expected 1536x1536 RGB, got {reference.shape}")
        if output.shape != (3072, 3072, 3):
            raise SystemExit(f"{output_path.name}: expected 3072x3072 RGB, got {output.shape}")

        for cell, (ref_cell, out_cell) in enumerate(zip(cells(reference, 512), cells(output, 1024))):
            silhouette, canopy, changed = compare(ref_cell, out_cell)
            ok = silhouette < 1.5 and canopy < 6.0 and changed > 3.0
            all_ok &= ok
            print(
                f"{output_path.name} cell {cell}: "
                f"silhouette={silhouette:.3f}% "
                f"canopy_blue_glass={canopy:.3f}% "
                f"changed={changed:.3f}% "
                f"{'PASS' if ok else 'FAIL'}"
            )

    raise SystemExit(0 if all_ok else 1)


if __name__ == "__main__":
    main()
