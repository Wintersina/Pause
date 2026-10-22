"""Audit the versioned Lance damaged/wreck sheets cell by cell."""

from pathlib import Path
from collections import deque

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
    """Mask the actual bright canopy, not the ship's cobalt wing armour."""

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
    candidate = (
        (hue >= 165)
        & (hue <= 215)
        & (saturation > 0.45)
        & (g > 100)
        & (b > 100)
        & (ratio > 0.58)
        & (ratio < 1.08)
    )
    seen = np.zeros(candidate.shape, dtype=bool)
    components: list[list[tuple[int, int]]] = []
    height, width = candidate.shape
    for y, x in zip(*np.where(candidate)):
        if seen[y, x]:
            continue
        seen[y, x] = True
        queue = deque([(int(y), int(x))])
        component: list[tuple[int, int]] = []
        while queue:
            cy, cx = queue.popleft()
            component.append((cy, cx))
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                ny, nx = cy + dy, cx + dx
                if 0 <= ny < height and 0 <= nx < width and candidate[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True
                    queue.append((ny, nx))
        components.append(component)

    eligible = []
    for component in components:
        ys = [p[0] for p in component]
        xs = [p[1] for p in component]
        bbox_width = max(xs) - min(xs) + 1
        if len(component) >= 200 and bbox_width <= 40 and min(ys) < 230:
            eligible.append(component)
    if not eligible:
        return np.zeros(candidate.shape, dtype=bool)
    chosen = max(eligible, key=len)
    result = np.zeros(candidate.shape, dtype=bool)
    ys, xs = zip(*chosen)
    result[np.array(ys), np.array(xs)] = True
    return result


def compare(reference: np.ndarray, output: np.ndarray) -> tuple[float, float, float]:
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
    for state in (1, 2):
        reference_path = ROOT / f"skinref_state{state}.png"
        output_path = ROOT / f"skin_Lance_state{state}_v2.png"
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
