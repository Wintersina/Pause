#!/usr/bin/env python3
"""Build the Storm colourway from the approved pixel-locked flipbook.

This helper is intentionally additive: it reads the approved 512 px cells,
paints only masked hull-marking pixels, and exports a 2x nearest-neighbour
sheet.  Existing helpers in this directory are left untouched.
"""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)
AMBER = np.array([0xFF, 0xB4, 0x3C], dtype=np.uint8)
ORANGE = np.array([0xF2, 0x86, 0x2B], dtype=np.uint8)
CREAM = np.array([0xF4, 0xEA, 0xD4], dtype=np.uint8)
INK = np.array([0x05, 0x06, 0x0C], dtype=np.uint8)
BONE_HOT = np.array([0xFF, 0xFD, 0xF4], dtype=np.uint8)
BONE_LIGHT = np.array([0xF4, 0xEA, 0xD4], dtype=np.uint8)
BONE_MID = np.array([0xE8, 0xDF, 0xCF], dtype=np.uint8)
BONE_SHADOW = np.array([0xCD, 0xC6, 0xBA], dtype=np.uint8)


def hsv_image(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    values = rgb.astype(np.float32) / 255.0
    red, green, blue = values[..., 0], values[..., 1], values[..., 2]
    maximum = values.max(axis=2)
    minimum = values.min(axis=2)
    delta = maximum - minimum
    hue = np.zeros_like(maximum)
    nonzero = delta > 1e-6
    red_max = nonzero & (maximum == red)
    green_max = nonzero & (maximum == green)
    blue_max = nonzero & (maximum == blue)
    hue[red_max] = ((green[red_max] - blue[red_max]) / delta[red_max]) % 6.0
    hue[green_max] = ((blue[green_max] - red[green_max]) / delta[green_max]) + 2.0
    hue[blue_max] = ((red[blue_max] - green[blue_max]) / delta[blue_max]) + 4.0
    hue = hue * 60.0
    saturation = np.zeros_like(maximum)
    saturation[maximum > 1e-6] = delta[maximum > 1e-6] / maximum[maximum > 1e-6]
    return hue, saturation, maximum


def blue_armour_mask(cell: np.ndarray) -> np.ndarray:
    hue, sat, value = hsv_image(cell)
    blue = (
        (hue >= 195.0) & (hue <= 285.0) & (sat >= 0.20) &
        (value >= 0.08) & np.any(cell != GREEN, axis=2)
    )
    yy, xx = np.indices(blue.shape)
    canopy = (
        (xx >= 155) & (xx < 357) & (yy >= 62) & (yy < 250) &
        (hue >= 150.0) & (hue < 204.0) & (sat >= 0.16) & (value > 0.10)
    )
    return blue & ~canopy


def stroke_mask(segments: list[tuple[tuple[int, int], tuple[int, int]]], width: int) -> np.ndarray:
    image = Image.new("L", (512, 512), 0)
    draw = ImageDraw.Draw(image)
    for start, end in segments:
        draw.line([start, end], fill=255, width=width)
    return np.array(image, dtype=bool)


def chevron(apex: tuple[int, int], half_width: int, rise: int) -> tuple[list[tuple[tuple[int, int], tuple[int, int]]], list[tuple[tuple[int, int], tuple[int, int]]]]:
    left = (apex[0] - half_width, apex[1] + rise)
    right = (apex[0] + half_width, apex[1] + rise)
    return [(left, apex), (apex, right)], [(left, (apex[0], apex[1] + 1)), ((apex[0], apex[1] + 1), right)]


def paint_chevron(out: np.ndarray, source: np.ndarray, apex: tuple[int, int], half_width: int, rise: int) -> None:
    armour = blue_armour_mask(source)
    segments, _ = chevron(apex, half_width, rise)
    lower = stroke_mask(segments, 18)
    face = stroke_mask(segments, 13)
    # A short upper rim gives the painted marking a crisp pixel-art highlight.
    highlight_segments = [((a[0], a[1] - 2), (b[0], b[1] - 2)) for a, b in segments]
    highlight = stroke_mask(highlight_segments, 2)
    out[lower & armour] = ORANGE
    out[face & armour] = AMBER
    out[highlight & armour] = CREAM


def front_marks(out: np.ndarray, source: np.ndarray) -> None:
    # Three separated, progressively narrower Vs on the rear central plating.
    for apex, half_width in [((256, 265), 86), ((256, 296), 76), ((256, 327), 66)]:
        paint_chevron(out, source, apex, half_width, 28)
    # Small wing Vs, inboard of the cream bolt lights and tip diamonds.
    paint_chevron(out, source, (128, 246), 31, 15)
    paint_chevron(out, source, (384, 246), 31, 15)


def bank_left_marks(out: np.ndarray, source: np.ndarray) -> None:
    for apex, half_width in [((205, 267), 58), ((214, 297), 51), ((222, 327), 43)]:
        paint_chevron(out, source, apex, half_width, 24)
    paint_chevron(out, source, (83, 252), 24, 13)
    paint_chevron(out, source, (354, 304), 27, 14)


def bank_right_marks(out: np.ndarray, source: np.ndarray) -> None:
    for apex, half_width in [((300, 267), 58), ((292, 297), 51), ((284, 327), 43)]:
        paint_chevron(out, source, apex, half_width, 24)
    paint_chevron(out, source, (157, 252), 27, 14)
    paint_chevron(out, source, (427, 304), 24, 13)


def repaint_hit_flash(cell: np.ndarray) -> np.ndarray:
    out = cell.copy()
    r, g, b = cell.astype(np.int16).transpose(2, 0, 1)
    bone = (
        (r > 145) & (g > 140) & (b > 125) & (r >= g - 4) &
        (g >= b - 4) & np.any(cell != GREEN, axis=2)
    )
    yy, xx = np.indices(bone.shape)
    bone &= ~((xx >= 145) & (xx < 367) & (yy >= 52) & (yy < 252))
    lum = cell.astype(np.float32).mean(axis=2)
    out[bone & (lum >= 242)] = BONE_HOT
    out[bone & (lum >= 205) & (lum < 242)] = BONE_LIGHT
    out[bone & (lum >= 165) & (lum < 205)] = BONE_MID
    out[bone & (lum < 165)] = BONE_SHADOW
    return out


def remove_magenta_fringe(rgb: np.ndarray) -> np.ndarray:
    out = rgb.copy()
    hue, sat, value = hsv_image(rgb)
    bad = (
        (hue >= 285.0) & (hue < 345.0) & (sat > 0.25) &
        np.any(rgb != GREEN, axis=2)
    )
    lum = (
        rgb[..., 0].astype(np.float32) * 0.2126 +
        rgb[..., 1].astype(np.float32) * 0.7152 +
        rgb[..., 2].astype(np.float32) * 0.0722
    )
    out[bad & (lum < 38.0)] = INK
    out[bad & (lum >= 38.0) & (lum < 105.0)] = np.array([0x5C, 0x18, 0x18], dtype=np.uint8)
    out[bad & (lum >= 105.0)] = np.array([0xAA, 0x1E, 0x22], dtype=np.uint8)
    return out


def build_sheet(state: int) -> None:
    source_path = ROOT / f"skinref_state{state}.png"
    target_path = ROOT / f"skin_Storm_state{state}.png"
    reference = np.array(Image.open(source_path).convert("RGB"), dtype=np.uint8)
    painted = reference.copy()
    for row in range(3):
        for col in range(3):
            y0, y1 = row * 512, (row + 1) * 512
            x0, x1 = col * 512, (col + 1) * 512
            cell = reference[y0:y1, x0:x1]
            out = cell.copy()
            if (row, col) == (2, 2):
                out = repaint_hit_flash(cell)
            elif (row, col) == (2, 0):
                bank_left_marks(out, cell)
            elif (row, col) == (2, 1):
                bank_right_marks(out, cell)
            else:
                front_marks(out, cell)
            painted[y0:y1, x0:x1] = out
    painted = remove_magenta_fringe(painted)
    enlarged = np.repeat(np.repeat(painted, 2, axis=0), 2, axis=1)
    Image.fromarray(enlarged, mode="RGB").save(target_path, optimize=True)


def main() -> None:
    for state in range(3):
        build_sheet(state)


if __name__ == "__main__":
    main()
