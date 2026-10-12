"""Cell and palette cleanup for the image-generated Paragon flame paintings.

Run from any directory: python3 tools/build_j2.py
Only nearest-neighbour resampling is used. The input flame drawings live beside
this script so the two sheets can be rebuilt without external image paths.
"""

from pathlib import Path

import numpy as np
from PIL import Image


HERE = Path(__file__).resolve().parent
OUT = HERE.parent
CELL_W, CELL_H = 256, 1024
GREEN = (0, 255, 0)
PALETTE = np.array(
    [GREEN, (23, 76, 140), (46, 155, 230), (110, 242, 238),
     (244, 234, 212), (154, 176, 255)],
    dtype=np.int16,
)
PALETTE_RGB = np.asarray(PALETTE, dtype=np.uint8)
NN = Image.Resampling.NEAREST


def painted_cells(source: Path):
    image = Image.open(source).convert("RGB")
    result = []
    for frame in range(7):
        left = round(frame * image.width / 7)
        right = round((frame + 1) * image.width / 7)
        original = np.asarray(image.crop((left, 0, right, image.height)), dtype=np.int16)
        # The generated background is close to green but not pixel exact. Pick
        # the nearest of the six requested colours, then resize without AA.
        delta = original[:, :, None, :].astype(np.int32) - PALETTE[None, None, :, :]
        index = np.argmin(np.sum(delta * delta, axis=3, dtype=np.int32), axis=2)
        cleaned = Image.fromarray(PALETTE_RGB[index], "RGB")
        result.append(cleaned)
    return result


def fit(cell: Image.Image, frame: int, target_tail: int, boost: bool):
    source = np.asarray(cell)
    if frame == 2:
        # The one-tick streak keeps the generated silhouette while collapsing
        # its painted blue bands into a hotter, nearly solid cyan-white core.
        streak = source.copy()
        streak[np.all(source == (46, 155, 230), axis=2)] = (110, 242, 238)
        streak[np.all(source == (110, 242, 238), axis=2)] = (244, 234, 212)
        source = streak
        cell = Image.fromarray(source, "RGB")
    painted = np.any(source != GREEN, axis=2)
    ys, xs = np.nonzero(painted)
    head_y, tail_y = int(ys.min()), int(ys.max())
    # Anchor to the incandescent head rather than to the drifting sparks.
    hot = (np.all(source == (244, 234, 212), axis=2) |
           np.all(source == (110, 242, 238), axis=2))
    head_hot = hot & (np.indices(hot.shape)[0] < head_y + 180)
    head_x = float(np.median(np.nonzero(head_hot)[1])) if head_hot.any() else float(np.median(xs))
    top = 31
    scale_y = (target_tail - top) / (tail_y - head_y)
    scale_x = (0.93 if boost else 0.85) * (0.76 if frame == 2 else 1.0)
    width = round(cell.width * scale_x)
    height = round(cell.height * scale_y)
    fitted = cell.resize((width, height), NN)
    canvas = Image.new("RGB", (CELL_W, CELL_H), GREEN)
    x = round(CELL_W / 2 - head_x * scale_x)
    y = round(top - head_y * scale_y)
    canvas.paste(fitted, (x, y))
    return canvas


def build(name: str, source_name: str, tails: list[int], boost: bool):
    cells = [fit(cell, i, tails[i], boost)
             for i, cell in enumerate(painted_cells(HERE / source_name))]
    sheet = Image.new("RGB", (7 * CELL_W, CELL_H), GREEN)
    for i, cell in enumerate(cells):
        sheet.paste(cell, (i * CELL_W, 0))
    sheet.save(OUT / name)
    return sheet


def darken_key(sheet: Image.Image):
    rgb = np.asarray(sheet).copy()
    rgb[np.all(rgb == GREEN, axis=2)] = (11, 11, 26)
    return Image.fromarray(rgb, "RGB")


def main():
    cruise = build("flame_cruise.png", "j2_painted_cruise.png",
                   [814, 801, 925, 821, 839, 807, 828], False)
    boost = build("flame_boost.png", "j2_painted_boost.png",
                  [985, 974, 1008, 982, 991, 972, 987], True)
    preview = Image.new("RGB", (7 * CELL_W, 2 * CELL_H + 32), (11, 11, 26))
    preview.paste(darken_key(cruise), (0, 0))
    preview.paste(darken_key(boost), (0, CELL_H + 32))
    preview.save(OUT / "preview_j2.png")


if __name__ == "__main__":
    main()
