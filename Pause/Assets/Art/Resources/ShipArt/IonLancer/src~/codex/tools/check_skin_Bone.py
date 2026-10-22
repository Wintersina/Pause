"""Audit Bone IonLancer sheets against the approved 3x3 references."""

from pathlib import Path

import numpy as np
from PIL import Image

from build_skin_Bone import material_masks


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


def compare(reference: np.ndarray, output: np.ndarray):
    # Final sheets are 2x nearest-neighbour exports.  Audit at source-cell
    # resolution so metrics describe drawing drift, not deliberate scaling.
    reduced = output[::2, ::2]
    ref_mask = np.any(reference != GREEN, axis=2)
    out_mask = np.any(reduced != GREEN, axis=2)
    silhouette_diff = np.mean(ref_mask != out_mask) * 100.0

    _armour, glass_mask, _hue, _sat, _value, _r, _g, _b = material_masks(reference)
    glass_pixels = np.any(reference[glass_mask] != reduced[glass_mask], axis=1)
    canopy_diff = np.mean(glass_pixels) * 100.0 if glass_pixels.size else 0.0
    changed = np.any(reference != reduced, axis=2)
    changed_fraction = np.mean(changed) * 100.0
    return silhouette_diff, canopy_diff, changed_fraction


def main() -> None:
    all_ok = True
    summary = []
    for state in range(3):
        reference = read(ROOT / f"skinref_state{state}.png")
        output_path = ROOT / f"skin_Bone_state{state}.png"
        output = read(output_path)
        if output.shape != (3072, 3072, 3):
            raise SystemExit(f"{output_path.name}: expected 3072x3072 RGB, got {output.shape}")

        for cell, (ref_cell, out_cell) in enumerate(zip(cells(reference, 512), cells(output, 1024))):
            metrics = compare(ref_cell, out_cell)
            ok = metrics[0] < 1.5 and metrics[1] < 6.0 and metrics[2] > 12.0
            all_ok &= ok
            summary.append(metrics)
            print(
                f"{output_path.name} cell {cell}: "
                f"silhouette={metrics[0]:.3f}% "
                f"canopy_blue_glass={metrics[1]:.3f}% "
                f"changed={metrics[2]:.3f}% "
                f"{'PASS' if ok else 'FAIL'}"
            )
    print(f"summary: {sum(1 for m in summary if m[0] < 1.5 and m[1] < 6.0 and m[2] > 12.0)}/27 cells pass")
    raise SystemExit(0 if all_ok else 1)


if __name__ == "__main__":
    main()
