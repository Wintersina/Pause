"""Validate the six generated Paragon guided-lance frames."""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
CELL = 512
GREEN = (0, 255, 0)
INK = (20, 12, 20)
COLORS = {
    GREEN, INK, (46, 155, 230), (23, 76, 140),
    (110, 242, 238), (244, 234, 212),
}


def bounds(mask: np.ndarray) -> tuple[int, int, int, int]:
    ys, xs = np.where(mask)
    if not len(xs):
        raise AssertionError("Empty sprite cell")
    return int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())


def main() -> None:
    sheet = np.asarray(Image.open(ROOT / "shot_6.png").convert("RGB"))
    assert sheet.shape == (2 * CELL, 3 * CELL, 3), sheet.shape
    found = set(map(tuple, sheet.reshape(-1, 3)))
    assert found <= COLORS, f"Unexpected colors: {found - COLORS}"
    cells = [sheet[i // 3 * CELL:(i // 3 + 1) * CELL,
                   i % 3 * CELL:(i % 3 + 1) * CELL] for i in range(6)]
    coverage = []
    ink_heights = []
    print("cell | non-green | sprite height | ink body height")
    for i, cell in enumerate(cells):
        non_green = np.any(cell != GREEN, axis=2)
        ink = np.all(cell == INK, axis=2)
        # The painted exhaust can include dark edge pixels; body measurement
        # stops at the shared nozzle line for each pose family.
        ink[352 if i >= 4 else 305:] = False
        _, top, _, bottom = bounds(non_green)
        _, ink_top, _, ink_bottom = bounds(ink)
        pct = 100 * non_green.mean()
        coverage.append(pct)
        ink_heights.append(ink_bottom - ink_top + 1)
        print(f"{i:>4} | {pct:>8.2f}% | {bottom - top + 1:>13} | {ink_heights[-1]:>15}")
    for i in range(4):
        assert coverage[i] >= 5, f"Cell {i}: coverage {coverage[i]:.2f}% < 5%"

    print("pair | differing pixels")
    for i in range(4):
        for j in range(i + 1, 4):
            pct = 100 * np.any(cells[i] != cells[j], axis=2).mean()
            print(f"{i}-{j:>1}  | {pct:>8.2f}%")
            assert pct >= 3, f"Cells {i}-{j}: difference {pct:.2f}% < 3%"

    loop_max = max(ink_heights[:4])
    for i in (4, 5):
        assert ink_heights[i] >= loop_max * 1.25, (
            f"Smear {i} ink body ({ink_heights[i]}) must be at least 25% "
            f"longer than loop body ({loop_max})"
        )
    print("PASS: coverage, every loop pair, palette, and longer smear bodies")


if __name__ == "__main__":
    main()
