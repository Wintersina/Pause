"""Audit the J4 4x4 Aegis meter atlas and its charge progression."""

from pathlib import Path
import sys

import numpy as np
from PIL import Image


ATLAS = Path(__file__).resolve().parent.parent / "meter_16.png"
GREEN = np.array([0, 255, 0], dtype=np.uint8)


def main() -> int:
    image = np.asarray(Image.open(ATLAS).convert("RGB"))
    height, width = image.shape[:2]
    if width != height or width % 4:
        print(f"FAIL: expected a square 4x4 atlas; got {width}x{height}")
        return 1

    cell_size = width // 4
    cells = [
        image[row * cell_size : (row + 1) * cell_size,
              col * cell_size : (col + 1) * cell_size]
        for row in range(4)
        for col in range(4)
    ]
    yy, xx = np.mgrid[:cell_size, :cell_size]
    distance = np.hypot(xx - (cell_size - 1) / 2, yy - (cell_size - 1) / 2)
    # The segmented circular track occupies this annulus in a 320 px cell.
    annulus = (distance >= cell_size * 112 / 320) & (distance <= cell_size * 142 / 320)
    arc_counts = []
    non_green = []
    for cell in cells:
        non_green.append(np.mean(np.any(cell != GREEN, axis=2)) * 100)
        red = cell[:, :, 0].astype(np.int16)
        green = cell[:, :, 1].astype(np.int16)
        blue = cell[:, :, 2].astype(np.int16)
        cyan = (blue > 130) & (green > 110) & (green * 10 > red * 14) & (blue * 10 > red * 13)
        arc_counts.append(int(np.count_nonzero(cyan & annulus)))

    pixel_diffs = [None] + [
        np.mean(np.any(cells[index] != cells[index - 1], axis=2)) * 100
        for index in range(1, 16)
    ]
    print(f"Atlas: {width}x{height}; cell: {cell_size}x{cell_size}")
    print("frame  non-green %  cyan arc px  arc Δ  pixel diff vs previous %")
    for frame in range(16):
        arc = str(arc_counts[frame]) if frame < 10 else "—"
        arc_delta = str(arc_counts[frame] - arc_counts[frame - 1]) if 0 < frame < 10 else "—"
        pixel_delta = f"{pixel_diffs[frame]:.2f}" if frame else "—"
        print(f"{frame:>5}  {non_green[frame]:>11.2f}  {arc:>11}  {arc_delta:>5}  {pixel_delta:>24}")

    increasing = all(arc_counts[i] < arc_counts[i + 1] for i in range(9))
    distinct = all(delta > 0 for delta in pixel_diffs[1:])
    print(f"Strictly increasing fill arc: {'PASS' if increasing else 'FAIL'}")
    print(f"All consecutive frames visibly distinct: {'PASS' if distinct else 'FAIL'}")
    return 0 if increasing and distinct else 1


if __name__ == "__main__":
    sys.exit(main())
