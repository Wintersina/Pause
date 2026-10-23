#!/usr/bin/env python3
"""Build J1b pose sheets from the approved keys and painted bank candidates.

The bank source panels are generated pixel art.  Cleanup is limited to exact
green keying, uniform nearest-neighbour sizing, and a few hand-painted light
and glint pixels for the requested animation cells.
"""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
GREEN = np.array([0, 255, 0], dtype=np.uint8)
CANVAS = 1024


def read(path: Path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGB"), dtype=np.uint8).copy()


def green_like(image: np.ndarray) -> np.ndarray:
    r = image[:, :, 0].astype(np.int16)
    g = image[:, :, 1].astype(np.int16)
    b = image[:, :, 2].astype(np.int16)
    return (g > 100) & (g - r > 25) & (g - b > 25)


def green_mask(image: np.ndarray) -> np.ndarray:
    return np.all(image == GREEN, axis=2)


def ship_mask(image: np.ndarray) -> np.ndarray:
    return ~green_mask(image)


def canopy_shape() -> np.ndarray:
    mask = Image.new("1", (CANVAS, CANVAS), 0)
    draw = ImageDraw.Draw(mask)
    draw.polygon(
        [(493, 238), (544, 245), (567, 326), (559, 425), (535, 472),
         (487, 472), (460, 420), (447, 325)],
        fill=1,
    )
    return np.asarray(mask, dtype=bool)


def source_panel(path: Path, panel: int) -> np.ndarray:
    source = read(path)
    width = source.shape[1] // 3
    crop = source[:, panel * width:(panel + 1) * width]
    side = min(crop.shape[:2])
    top = max(0, (crop.shape[0] - side) // 2)
    left = max(0, (crop.shape[1] - side) // 2)
    crop = crop[top:top + side, left:left + side]
    crop[green_like(crop)] = GREEN
    result = np.asarray(
        Image.fromarray(crop, mode="RGB").resize((CANVAS, CANVAS), Image.Resampling.NEAREST),
        dtype=np.uint8,
    ).copy()
    result[green_like(result)] = GREEN
    return result


def bbox(mask: np.ndarray) -> tuple[int, int, int, int] | None:
    y, x = np.where(mask)
    if not len(x):
        return None
    return int(x.min()), int(y.min()), int(x.max() + 1), int(y.max() + 1)


def fit_bank(bank: np.ndarray, key: np.ndarray, direction: int) -> np.ndarray:
    """Keep the generated bank at a key-sized scale without widening idle."""
    key_box = bbox(ship_mask(key))
    bank_box = bbox(ship_mask(bank))
    assert key_box and bank_box
    key_width = key_box[2] - key_box[0]
    bank_width = bank_box[2] - bank_box[0]
    scale = min(1.0, 0.99 * key_width / bank_width)
    if scale < 0.999:
        side = max(1, int(round(CANVAS * scale)))
        resized = np.asarray(
            Image.fromarray(bank, mode="RGB").resize((side, side), Image.Resampling.NEAREST),
            dtype=np.uint8,
        )
        bank = np.full((CANVAS, CANVAS, 3), GREEN, dtype=np.uint8)
        offset = (CANVAS - side) // 2
        bank[offset:offset + side, offset:offset + side] = resized
    # Match the approved key anchor while shifting the centreline only a
    # small amount toward the bank direction.
    bank_box = bbox(ship_mask(bank))
    assert bank_box
    key_cx = (key_box[0] + key_box[2]) // 2
    key_cy = (key_box[1] + key_box[3]) // 2
    bank_cx = (bank_box[0] + bank_box[2]) // 2
    bank_cy = (bank_box[1] + bank_box[3]) // 2
    target_cx = key_cx + (-14 if direction < 0 else 14)
    dx = target_cx - bank_cx
    dy = key_cy - bank_cy
    shifted = np.full_like(bank, GREEN)
    sy0, sy1 = max(0, -dy), min(CANVAS, CANVAS - dy)
    sx0, sx1 = max(0, -dx), min(CANVAS, CANVAS - dx)
    shifted[sy0 + dy:sy1 + dy, sx0 + dx:sx1 + dx] = bank[sy0:sy1, sx0:sx1]
    return shifted


def darken_lights(key: np.ndarray) -> np.ndarray:
    out = key.copy()
    r = out[:, :, 0].astype(np.int16)
    g = out[:, :, 1].astype(np.int16)
    b = out[:, :, 2].astype(np.int16)
    redish = (r > 100) & (r > g * 1.45) & (r > b * 1.15)
    zones = np.zeros(redish.shape, dtype=bool)
    zones[540:760, 120:320] = True
    zones[540:760, 700:905] = True
    zones[730:845, 35:130] = True
    zones[730:845, 895:990] = True
    bolt = redish & zones
    luminance = (r + g + b) // 3
    out[bolt & (luminance > 100)] = (72, 20, 34)
    out[bolt & (luminance <= 100)] = (26, 28, 52)

    star = np.zeros(redish.shape, dtype=bool)
    star[700:790, 465:560] = True
    star &= (r > 70) & (g > 45) & (b < 115)
    out[star & (luminance > 110)] = (83, 58, 17)
    out[star & (luminance <= 110)] = (34, 34, 36)

    nozzles = np.zeros(redish.shape, dtype=bool)
    nozzles[905:972, 285:385] = True
    nozzles[905:972, 640:740] = True
    nozzle = nozzles & (r > 80) & (g > 38) & (b < 95)
    out[nozzle & (luminance > 120)] = (64, 39, 18)
    out[nozzle & (luminance <= 120)] = (31, 31, 38)

    glass = canopy_shape()
    teal = glass & (g > 40) & (b > 50) & (g > r + 15) & (b > r + 20)
    ramp = np.array(
        [(7, 24, 40), (9, 44, 63), (11, 79, 103), (13, 145, 179), (16, 193, 218)],
        dtype=np.uint8,
    )
    lum = (r + g + b) / 3.0
    bins = np.digitize(lum, [50, 90, 135, 185])
    bins = np.clip(bins - 1, 0, len(ramp) - 1)
    out[teal] = ramp[bins[teal]]
    return out


def paint_glint(key: np.ndarray, side: str, faint: bool = False) -> np.ndarray:
    out = key.copy()
    glass = canopy_shape()
    yy, xx = np.indices((CANVAS, CANVAS))
    if side == "left":
        # Lower-left to upper-right, across the left half of the canopy.
        center = 536.0 - (yy - 260.0) * 0.39
    else:
        # A thinner, fainter pass on the right half.
        center = 557.0 - (yy - 275.0) * 0.25
    width = 29.0 if not faint else 10.0
    streak = glass & (np.abs(xx - center) <= width)
    if not faint:
        out[streak] = (245, 253, 253)
        inner = glass & (np.abs(xx - center) <= 5.0)
        out[inner] = (255, 251, 224)
        sparkle = glass & (((xx - 535) ** 2 + (yy - 258) ** 2) <= 24 ** 2)
        out[sparkle] = (245, 253, 253)
        out[glass & (np.abs(xx - 535) <= 4) & (np.abs(yy - 258) <= 30)] = (255, 251, 224)
    else:
        out[streak] = (104, 214, 226)
        inner = glass & (np.abs(xx - center) <= 2.0)
        out[inner] = (194, 240, 238)
    return out


def make_sheet(key: np.ndarray, bank_source: Path, state: int) -> np.ndarray:
    lights_off = darken_lights(key)
    glint1 = paint_glint(key, "left")
    glint2 = paint_glint(lights_off, "right", faint=True)
    bank_left = fit_bank(source_panel(bank_source, 1), key, -1)
    bank_right = fit_bank(source_panel(bank_source, 2), key, 1)
    sheet = np.full((CANVAS * 2, CANVAS * 3, 3), GREEN, dtype=np.uint8)
    cells = [lights_off, glint1, glint2, bank_left, bank_right, np.full_like(key, GREEN)]
    for index, cell in enumerate(cells):
        row, col = divmod(index, 3)
        sheet[row * CANVAS:(row + 1) * CANVAS, col * CANVAS:(col + 1) * CANVAS] = cell
    return sheet


def main() -> None:
    for state in range(3):
        key = read(ROOT / f"key_state{state}.png")
        candidate = ROOT / ["candidate_j1b_intact.png", "candidate_j1b_state1.png", "candidate_j1b_state2.png"][state]
        sheet = make_sheet(key, candidate, state)
        Image.fromarray(sheet, mode="RGB").save(ROOT / f"hull_redo_state{state}.png")

    # Dark preview: three sheets side by side, preserving every pose cell.
    preview = Image.new("RGB", (CANVAS * 3, CANVAS * 2), (11, 11, 26))
    for state in range(3):
        sheet = Image.open(ROOT / f"hull_redo_state{state}.png").convert("RGB")
        pixels = np.asarray(sheet).copy()
        pixels[np.all(pixels == GREEN, axis=2)] = (11, 11, 26)
        preview.paste(Image.fromarray(pixels, mode="RGB"), (state * CANVAS, 0))
    preview.save(ROOT / "preview_j1b.png")


if __name__ == "__main__":
    main()
