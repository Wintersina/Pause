"""Audit the new Sodium damaged/wreck sheets against the approved references."""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)


def read(path: Path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGB"), dtype=np.uint8)


def hsv_arrays(rgb: np.ndarray):
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
    saturation = np.divide(delta, mx, out=np.zeros_like(delta), where=mx > 0)
    return hue, saturation


def canopy_mask(rgb: np.ndarray) -> np.ndarray:
    hue, sat = hsv_arrays(rgb)
    return (
        (hue >= 165)
        & (hue <= 215)
        & (sat > 0.45)
        & (rgb[..., 1] > 20)
        & ((rgb[..., 1] / np.maximum(rgb[..., 2], 1)) > 0.58)
        & ((rgb[..., 1] / np.maximum(rgb[..., 2], 1)) < 1.08)
    )


def cells(sheet: np.ndarray, size: int) -> list[np.ndarray]:
    return [
        sheet[y : y + size, x : x + size]
        for y in range(0, sheet.shape[0], size)
        for x in range(0, sheet.shape[1], size)
    ]


def compare(reference: np.ndarray, output: np.ndarray) -> tuple[float, float, float]:
    reduced = output[::2, ::2]
    ref_mask = np.any(reference != GREEN, axis=2)
    out_mask = np.any(reduced != GREEN, axis=2)
    silhouette_diff = float(np.mean(ref_mask != out_mask) * 100.0)

    glass = canopy_mask(reference)
    canopy_diff = (
        float(np.mean(np.any(reference[glass] != reduced[glass], axis=1)) * 100.0)
        if np.any(glass)
        else 0.0
    )
    changed = float(np.mean(np.any(reference != reduced, axis=2)) * 100.0)
    return silhouette_diff, canopy_diff, changed


def main() -> None:
    all_ok = True
    summary = []
    for state in (1, 2):
        reference_path = ROOT / f"skinref_state{state}.png"
        output_path = ROOT / f"skin_Sodium_state{state}_v2.png"
        reference = read(reference_path)
        output = read(output_path)
        if reference.shape != (1536, 1536, 3):
            raise SystemExit(f"{reference_path.name}: expected 1536x1536 RGB, got {reference.shape}")
        if output.shape != (3072, 3072, 3):
            raise SystemExit(f"{output_path.name}: expected 3072x3072 RGB, got {output.shape}")

        state_metrics = []
        for cell, (ref_cell, out_cell) in enumerate(zip(cells(reference, 512), cells(output, 1024))):
            metrics = compare(ref_cell, out_cell)
            ok = metrics[0] < 1.5 and metrics[1] < 6.0 and metrics[2] > 12.0
            all_ok &= ok
            state_metrics.append(metrics)
            print(
                f"{output_path.name} cell {cell}: "
                f"silhouette={metrics[0]:.3f}% "
                f"canopy_blue_glass={metrics[1]:.3f}% "
                f"changed={metrics[2]:.3f}% "
                f"{'PASS' if ok else 'FAIL'}"
            )
        summary.append((output_path.name, state_metrics))

    total = sum(len(metrics) for _, metrics in summary)
    passed = sum(
        1
        for _, metrics_list in summary
        for metrics in metrics_list
        if metrics[0] < 1.5 and metrics[1] < 6.0 and metrics[2] > 12.0
    )
    print(f"SUMMARY: {passed}/{total} cells PASS")
    raise SystemExit(0 if all_ok else 1)


if __name__ == "__main__":
    main()
