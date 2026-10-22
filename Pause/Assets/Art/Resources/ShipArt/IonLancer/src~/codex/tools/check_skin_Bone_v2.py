"""Audit the new Bone damaged and wreck sheets against approved references."""

from pathlib import Path

import numpy as np
from PIL import Image

from build_skin_Bone_v2 import cyan_reference_mask


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


def compare(reference: np.ndarray, output: np.ndarray, bone_cell: np.ndarray):
    reduced = output[::2, ::2]
    ref_mask = np.any(reference != GREEN, axis=2)
    out_mask = np.any(reduced != GREEN, axis=2)
    silhouette_diff = np.mean(ref_mask != out_mask) * 100.0

    glass = cyan_reference_mask(bone_cell)
    if glass.any():
        canopy_diff = np.mean(np.any(reference[glass] != reduced[glass], axis=1)) * 100.0
    else:
        canopy_diff = 0.0
    changed_fraction = np.mean(np.any(reference != reduced, axis=2)) * 100.0
    return silhouette_diff, canopy_diff, changed_fraction


def main() -> None:
    bone = cells(read(ROOT / "skin_Bone_state0.png")[::2, ::2], 512)
    all_ok = True
    passed = 0
    total = 0
    for state in (1, 2):
        reference = read(ROOT / f"skinref_state{state}.png")
        output_path = ROOT / f"skin_Bone_state{state}_v2.png"
        output = read(output_path)
        if output.shape != (3072, 3072, 3):
            raise SystemExit(f"{output_path.name}: expected 3072x3072 RGB, got {output.shape}")
        for index, (ref_cell, out_cell, bone_cell) in enumerate(
            zip(cells(reference, 512), cells(output, 1024), bone)
        ):
            metrics = compare(ref_cell, out_cell, bone_cell)
            ok = metrics[0] < 1.5 and metrics[1] < 6.0 and metrics[2] > 12.0
            all_ok &= ok
            passed += int(ok)
            total += 1
            print(
                f"{output_path.name} cell {index}: "
                f"silhouette={metrics[0]:.3f}% "
                f"canopy_blue_glass={metrics[1]:.3f}% "
                f"changed={metrics[2]:.3f}% "
                f"{'PASS' if ok else 'FAIL'}"
            )
    print(f"summary: {passed}/{total} cells pass")
    raise SystemExit(0 if all_ok else 1)


if __name__ == "__main__":
    main()
