"""Validate the three Paragon J1 3x3 hull flipbooks and print cell bounds."""

from pathlib import Path

import numpy as np
from PIL import Image


OUT = Path(__file__).resolve().parent.parent
GREEN = np.array((0, 255, 0), dtype=np.uint8)


def bounds(rgb):
    mask = np.any(rgb != GREEN, axis=2)
    if not mask.any():
        raise ValueError("empty cell: no ship pixels")
    y, x = np.where(mask)
    return (int(x.min()), int(y.min()), int(x.max()) + 1, int(y.max()) + 1)


def main():
    paths = [OUT / f"hull_state{i}.png" for i in range(3)]
    missing = [str(p) for p in paths if not p.is_file()]
    if missing:
        raise FileNotFoundError("missing sheets: " + ", ".join(missing))

    sizes = {Image.open(p).size for p in paths}
    if len(sizes) != 1:
        raise ValueError(f"sheet dimensions differ: {sizes}")
    sheet_w, sheet_h = sizes.pop()
    if sheet_w != sheet_h or sheet_w % 3:
        raise ValueError(f"not a 3x3 grid of equal square cells: {sheet_w}x{sheet_h}")
    cell = sheet_w // 3

    print(f"3 sheets | 3x3 cells | {cell}x{cell} px per cell")
    print("state  pose  bbox x0,y0,x1,y1       size wxh     size delta")
    for state, path in enumerate(paths):
        rgb = np.asarray(Image.open(path).convert("RGB"))
        baseline = None
        for pose in range(9):
            row, col = divmod(pose, 3)
            crop = rgb[row * cell:(row + 1) * cell, col * cell:(col + 1) * cell]
            box = bounds(crop)
            width, height = box[2] - box[0], box[3] - box[1]
            if baseline is None:
                baseline = (width, height)
            delta = max(abs(width / baseline[0] - 1), abs(height / baseline[1] - 1))
            print(f"  {state}     {pose}   {str(box):<23} {width:4}x{height:<4}    {delta:5.1%}")
            if delta > 0.03:
                raise ValueError(f"state {state} pose {pose} exceeds 3% size drift")
    print("PASS: all 27 cells contain a hull within 3% of their state's key pose size")


if __name__ == "__main__":
    main()
