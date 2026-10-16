from __future__ import annotations

import colorsys
import json
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
SOURCE = Path(__file__).resolve().parent / "candidate_ember_wind_c.png"
OUT = ROOT / "wx_gust.png"
PREVIEW = ROOT / "preview.png"
CELL = 512

# Ember ramps from docs/art-style.md, plus the shared ink. Quantising the generated
# painting to this set also removes the generator's occasional red fringe pixels.
PALETTE = [
    "05060c",
    "0e0808", "1d1212", "2e1d1a", "47302b", "6a4a40", "957060",
    "6a2404", "c24a06", "f77a0a", "fcb809", "fcee09", "fdfa92",
    "c24a06", "fc7a08", "fcb809", "fdf06a", "fffbe0",
]
PALETTE_RGB = [tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) for h in PALETTE]


def nearest_colour(rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    return min(PALETTE_RGB, key=lambda p: sum((a - b) ** 2 for a, b in zip(rgb, p)))


def quantize_alpha(alpha: int, scale: float) -> int:
    value = alpha * scale
    if value < 18:
        return 0
    if value < 64:
        return 38
    if value < 108:
        return 76
    if value < 150:
        return 114
    if value < 208:
        return 153
    if value < 242:
        return 204
    return 255


def process_cell(cell: Image.Image, alpha_scale: float) -> Image.Image:
    cell = cell.resize((CELL, CELL), Image.Resampling.NEAREST).convert("RGBA")
    pixels = []
    for r, g, b, a in cell.getdata():
        qa = quantize_alpha(a, alpha_scale)
        if qa == 0:
            pixels.append((0, 0, 0, 0))
        else:
            pixels.append((*nearest_colour((r, g, b)), qa))
    cell.putdata(pixels)
    return cell


def fit_content(cell: Image.Image, max_width: int, max_height: int, preserve_aspect: bool) -> Image.Image:
    bbox = cell.getchannel("A").getbbox()
    if bbox is None:
        return Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    art = cell.crop(bbox)
    if preserve_aspect:
        scale = min(max_width / art.width, max_height / art.height)
        size = (max(1, round(art.width * scale)), max(1, round(art.height * scale)))
    else:
        size = (max_width, max_height)
    art = art.resize(size, Image.Resampling.NEAREST)
    fitted = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    fitted.paste(art, ((CELL - art.width) // 2, (CELL - art.height) // 2))
    return fitted


def equalize_tile_edges(cell: Image.Image) -> None:
    """Make the seam alpha agree while keeping both sides' painted colour."""
    px = cell.load()
    for y in range(CELL):
        for offset in range(2):
            left = px[offset, y]
            right = px[CELL - 1 - offset, y]
            common_alpha = (left[3] + right[3] + 1) // 2
            if common_alpha == 0:
                left_value = right_value = (0, 0, 0, 0)
            else:
                if left[3] and right[3]:
                    rgb = tuple((left[i] + right[i] + 1) // 2 for i in range(3))
                elif left[3]:
                    rgb = left[:3]
                else:
                    rgb = right[:3]
                rgb = nearest_colour(rgb)
                left_value = (*rgb, common_alpha)
                right_value = (*rgb, common_alpha)
            px[offset, y] = left_value
            px[CELL - 1 - offset, y] = right_value


def crop_source(sheet: Image.Image, col: int, row: int) -> Image.Image:
    # The generator's 1774 x 887 result is exactly 2:1; round each boundary so
    # no resampling or free-layout crop can drift between cells.
    x0 = round(col * sheet.width / 4)
    x1 = round((col + 1) * sheet.width / 4)
    y0 = round(row * sheet.height / 2)
    y1 = round((row + 1) * sheet.height / 2)
    return sheet.crop((x0, y0, x1, y1))


def make_sheet() -> tuple[Image.Image, dict]:
    source = Image.open(SOURCE).convert("RGBA")
    if source.width / source.height != 2:
        raise ValueError(f"candidate must be 2:1, got {source.size}")
    sheet = Image.new("RGBA", (CELL * 4, CELL * 2), (0, 0, 0, 0))
    for row in range(2):
        for col in range(4):
            if row == 0:
                alpha_scale = 0.52
            elif col < 2:
                alpha_scale = 0.78
            else:
                alpha_scale = 1.0
            cell = process_cell(crop_source(source, col, row), alpha_scale)
            if row == 0:
                # Ribbons must stay in the middle 55% while still touching both
                # horizontal edges for repeatable tiling.
                cell = fit_content(cell, CELL, 282, preserve_aspect=False)
                equalize_tile_edges(cell)
            elif col < 2:
                # Telegraph fronts are intentionally compact, bold crescents.
                cell = fit_content(cell, 360, 360, preserve_aspect=True)
            else:
                # Debris is a small crisp sprite, not an ambient cloud.
                cell = fit_content(cell, 120, 120, preserve_aspect=True)
            sheet.paste(cell, (col * CELL, row * CELL))

    # Emit a small audit payload while the pixels are still in memory.
    cells = []
    for row in range(2):
        for col in range(4):
            cell = sheet.crop((col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL))
            alpha = cell.getchannel("A")
            bbox = alpha.getbbox()
            values = list(alpha.getdata())
            cells.append({
                "row": row,
                "col": col,
                "nonzero": sum(v > 0 for v in values),
                "bbox": bbox,
                "mean_nonzero_alpha": round(sum(v for v in values if v) / max(1, sum(v > 0 for v in values)), 2),
            })
    return sheet, {"size": sheet.size, "cells": cells}


def main() -> None:
    if OUT.exists() or PREVIEW.exists():
        raise FileExistsError("refusing to overwrite an existing gust asset or preview")
    sheet, audit = make_sheet()
    sheet.save(OUT, format="PNG", optimize=False)
    preview = Image.new("RGBA", sheet.size, (0x0B, 0x0B, 0x1A, 255))
    preview.alpha_composite(sheet)
    preview.save(PREVIEW, format="PNG", optimize=False)
    print(json.dumps(audit, indent=2))
    print(f"wrote {OUT}")
    print(f"wrote {PREVIEW}")


if __name__ == "__main__":
    main()
