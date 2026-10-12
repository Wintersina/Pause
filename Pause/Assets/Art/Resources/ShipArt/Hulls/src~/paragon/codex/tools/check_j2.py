"""Validate the two seven-cell Paragon engine flame sheets.

Run from any directory: python3 tools/check_j2.py
"""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
ALLOWED = {
    GREEN, (23, 76, 140), (46, 155, 230),
    (110, 242, 238), (244, 234, 212), (154, 176, 255),
}
HOLD_TICKS = (2, 2, 1, 2, 2, 2, 2)


def check_sheet(name: str, boost: bool):
    path = ROOT / name
    assert path.is_file(), f"Missing {path}"
    with Image.open(path) as opened:
        assert opened.mode in ("RGB", "RGBA"), (name, opened.mode)
        assert opened.size == (1792, 1024), (name, opened.size)
        rgb = np.asarray(opened.convert("RGB"))
        if opened.mode == "RGBA":
            assert np.all(np.asarray(opened)[:, :, 3] == 255), f"{name}: alpha is not opaque"
    assert rgb.shape[1] == 7 * (rgb.shape[0] // 4), f"{name}: unequal or non-1:4 cells"
    print(f"{name}  {rgb.shape[1]}x{rgb.shape[0]}  7 x 256x1024")
    print("frame hold  pixels  top%  tail%  width  colours")
    for frame in range(7):
        cell = rgb[:, frame * 256:(frame + 1) * 256]
        colours = {tuple(int(v) for v in p) for p in np.unique(cell.reshape(-1, 3), axis=0)}
        unexpected = colours - ALLOWED
        assert not unexpected, f"{name} frame {frame}: unexpected colours {unexpected}"
        assert tuple(int(v) for v in cell[0, 0]) == GREEN, f"{name} frame {frame}: key corner changed"
        mask = np.any(cell != GREEN, axis=2)
        ys, xs = np.nonzero(mask)
        assert len(ys), f"{name} frame {frame}: empty"
        top, tail = int(ys.min()), int(ys.max())
        assert 20 <= top <= 45, f"{name} frame {frame}: head at {top}"
        if boost:
            assert 960 <= tail <= 1015, f"{name} frame {frame}: boost tail at {tail}"
        elif frame == 2:
            assert 890 <= tail <= 950, f"{name} frame {frame}: cruise smear tail at {tail}"
        else:
            assert 770 <= tail <= 870, f"{name} frame {frame}: cruise tail at {tail}"
        assert xs.min() > 0 and xs.max() < 255, f"{name} frame {frame}: clipped sides"
        width = int(xs.max() - xs.min() + 1)
        print(f"{frame:>5} {HOLD_TICKS[frame]:>4} {len(ys):>7}  {top / 1024 * 100:>4.1f}  "
              f"{tail / 1024 * 100:>5.1f}  {width:>5}  {len(colours)}/6")
    print()


def main():
    check_sheet("flame_cruise.png", False)
    check_sheet("flame_boost.png", True)
    print("PASS: dimensions, cells, opaque chroma key, palette, and flame extents")


if __name__ == "__main__":
    main()
