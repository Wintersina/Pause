"""Re-cell the selected image-generated J3b painting with hard pixel operations."""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
SOURCE = Path(__file__).with_name("j3b_painted_source.png")
CELL = 512
GREEN = (0, 255, 0)
PALETTE = np.array(
    [(46, 155, 230), (23, 76, 140), (110, 242, 238),
     (244, 234, 212), (20, 12, 20)], dtype=np.int32
)


def clean(image: Image.Image) -> Image.Image:
    rgb = np.asarray(image.convert("RGB")).copy()
    r, g, b = (rgb[:, :, i].astype(np.int16) for i in range(3))
    green = (g > 100) & (g > r * 1.2) & (g > b * 1.1)
    for y in range(0, rgb.shape[0], 64):
        block = rgb[y:y + 64]
        mask = ~green[y:y + 64]
        if not np.any(mask):
            block[:] = GREEN
            continue
        pixels = block[mask].astype(np.int32)
        distance = ((pixels[:, None] - PALETTE[None]) ** 2).sum(axis=2)
        block[mask] = PALETTE[distance.argmin(axis=1)].astype(np.uint8)
        block[~mask] = GREEN
    return Image.fromarray(rgb, "RGB")


def scaled_strip(tile: Image.Image, top: int, bottom: int,
                 width: int, height: int) -> Image.Image:
    return tile.crop((0, top, CELL, bottom)).resize(
        (width, height), Image.Resampling.NEAREST
    )


def make_frame(tile: Image.Image, index: int) -> Image.Image:
    frame = Image.new("RGB", (CELL, CELL), GREEN)
    smear = index >= 4
    # The painted lance body and its generated exhaust are fitted separately
    # so the body and full sprite meet the requested 40/60 percent proportions.
    cut = 270 if smear else 276
    body_height = 274 if smear else 205
    body_width = 345 if smear else 435
    flame_height = (89 if index == 4 else 99) if smear else (102, 70, 102, 102)[index]
    body_top = 78 if smear else 100
    tile = clean(tile)
    body = scaled_strip(tile, 0 if smear else 16, cut, body_width, body_height)
    frame.paste(body, ((CELL - body_width) // 2, body_top))

    pixels = np.asarray(tile)
    non_green = np.any(pixels != GREEN, axis=2)
    ys = np.where(non_green[cut:].any(axis=1))[0]
    if len(ys):
        flame_end = cut + int(ys[-1]) + 1
        flame = scaled_strip(tile, cut, flame_end, body_width, flame_height)
        frame.paste(flame, ((CELL - body_width) // 2, body_top + body_height))
    return frame


def main() -> None:
    source = Image.open(SOURCE).convert("RGB")
    if source.size != (3 * CELL, 2 * CELL):
        raise ValueError(f"Expected a 3x2 sheet of {CELL}px cells: {source.size}")
    sheet = Image.new("RGB", (3 * CELL, 2 * CELL), GREEN)
    for index in range(6):
        x, y = index % 3, index // 3
        tile = source.crop((x * CELL, y * CELL, (x + 1) * CELL, (y + 1) * CELL))
        sheet.paste(make_frame(tile, index), (x * CELL, y * CELL))
    sheet.save(ROOT / "shot_6.png")

    preview = Image.new("RGB", sheet.size, (11, 11, 26))
    a = np.asarray(sheet)
    mask = Image.fromarray(np.uint8(np.any(a != GREEN, axis=2)) * 255, "L")
    preview.paste(sheet, (0, 0), mask)
    preview.save(ROOT / "preview_j3b.png")


if __name__ == "__main__":
    main()
