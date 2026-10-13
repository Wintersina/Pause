"""Re-cell the image-generated J4 painting with a hard chroma key.

The painted source is retained beside this helper. Every output pixel comes
from that painting via nearest-neighbour sampling, except keyed background.
"""

from pathlib import Path

import numpy as np
from PIL import Image


HERE = Path(__file__).resolve().parent
OUT = HERE.parent
SOURCE = HERE / "j4_painted_source.png"
GREEN = (0, 255, 0)
NATIVE_CELL = 80
EXPORT_SCALE = 4


def main() -> None:
    source = Image.open(SOURCE).convert("RGB")
    width, height = source.size
    native = Image.new("RGB", (4 * NATIVE_CELL, 4 * NATIVE_CELL), GREEN)
    painted_cells = []

    for frame in range(16):
        row, col = divmod(frame, 4)
        box = (
            round(col * width / 4),
            round(row * height / 4),
            round((col + 1) * width / 4),
            round((row + 1) * height / 4),
        )
        cell = source.crop(box).resize(
            (NATIVE_CELL, NATIVE_CELL), Image.Resampling.NEAREST
        )
        pixels = np.array(cell)
        red = pixels[:, :, 0].astype(np.float32)
        green = pixels[:, :, 1].astype(np.float32)
        blue = pixels[:, :, 2].astype(np.float32)
        # Include the near-black green fringe produced at the painted edge.
        chroma = (green > red * 1.25) & (green > blue * 1.15)
        pixels[chroma] = GREEN
        painted_cells.append(pixels)

    # The generated source painted both the dormant disc and a complete lit
    # track. Reveal that painted track while holding the disc still in 0-9.
    yy, xx = np.mgrid[:NATIVE_CELL, :NATIVE_CELL]
    dx = xx - (NATIVE_CELL - 1) / 2
    dy = yy - (NATIVE_CELL - 1) / 2
    radius = np.hypot(dx, dy)
    angle = (np.degrees(np.arctan2(dx, -dy)) + 360) % 360
    track = (radius >= 27) & (radius <= 36.5)
    for frame in range(1, 10):
        pixels = painted_cells[0].copy()
        reveal = track & (angle < 360 * frame / 9)
        pixels[reveal] = painted_cells[9][reveal]
        painted_cells[frame] = pixels

    for frame, pixels in enumerate(painted_cells):
        row, col = divmod(frame, 4)
        native.paste(
            Image.fromarray(pixels, "RGB"),
            (col * NATIVE_CELL, row * NATIVE_CELL),
        )

    atlas = native.resize(
        (native.width * EXPORT_SCALE, native.height * EXPORT_SCALE),
        Image.Resampling.NEAREST,
    )
    atlas.save(OUT / "meter_16.png")
    native.resize((640, 640), Image.Resampling.NEAREST).save(OUT / "preview_j4.png")


if __name__ == "__main__":
    main()
