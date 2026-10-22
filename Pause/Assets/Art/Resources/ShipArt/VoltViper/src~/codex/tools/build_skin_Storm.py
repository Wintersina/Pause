#!/usr/bin/env python3
"""Build the reference-locked VoltViper Storm livery sheets.

The approved sheets remain the drawing authority.  Storm paint is applied at
the reference's 512 px cell resolution, then exported at 2x with nearest
neighbour so pose anchors, damage, outlines, and all non-livery pixels remain
locked to the approved flipbook.
"""

from __future__ import annotations

import colorsys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)

AMBER = np.array([0xFF, 0xB4, 0x3C], dtype=np.uint8)
ORANGE = np.array([0xF2, 0x86, 0x2B], dtype=np.uint8)
CREAM = np.array([0xF4, 0xEA, 0xD4], dtype=np.uint8)
RED_DETAIL_DARK = np.array([0x5C, 0x18, 0x18], dtype=np.uint8)
RED_DETAIL_MID = np.array([0xAA, 0x1E, 0x22], dtype=np.uint8)
INK = np.array([0x05, 0x06, 0x0C], dtype=np.uint8)

# The hit-flash remains bone-white, but uses a restrained bone ramp so that
# the Storm export is still a real colourway in that otherwise white cell.
BONE_HOT = np.array([0xFF, 0xFD, 0xF4], dtype=np.uint8)
BONE_LIGHT = np.array([0xF4, 0xEA, 0xD4], dtype=np.uint8)
BONE_MID = np.array([0xE8, 0xDF, 0xCF], dtype=np.uint8)
BONE_SHADOW = np.array([0xCD, 0xC6, 0xBA], dtype=np.uint8)


def hsv_image(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    flat = rgb.reshape(-1, 3).astype(np.float32) / 255.0
    hsv = np.array([colorsys.rgb_to_hsv(*pixel) for pixel in flat], dtype=np.float32)
    hsv = hsv.reshape(rgb.shape)
    return hsv[..., 0] * 360.0, hsv[..., 1], hsv[..., 2]


def blue_armour_mask(cell: np.ndarray) -> np.ndarray:
    """Keep Storm paint on azure plating, never canopy, lights, or damage."""
    hue, sat, value = hsv_image(cell)
    blue = (
        (hue >= 195.0)
        & (hue <= 285.0)
        & (sat >= 0.20)
        & (value >= 0.08)
        & np.any(cell != GREEN, axis=2)
    )
    yy, xx = np.indices(blue.shape)
    canopy = (
        (xx >= 155)
        & (xx < 357)
        & (yy >= 62)
        & (yy < 250)
        & (hue >= 150.0)
        & (hue < 204.0)
        & (sat >= 0.16)
        & (value > 0.10)
    )
    return blue & ~canopy


def line_mask(points: list[tuple[int, int]], width: int) -> np.ndarray:
    mask = Image.new("L", (512, 512), 0)
    ImageDraw.Draw(mask).line(points, fill=255, width=width, joint="curve")
    return np.array(mask, dtype=bool)


def paint_layer(out: np.ndarray, source: np.ndarray, shape: np.ndarray, colour: np.ndarray) -> None:
    out[shape & blue_armour_mask(source)] = colour


def paint_storm_chevrons(cell: np.ndarray) -> np.ndarray:
    out = cell.copy()
    armour = blue_armour_mask(cell)

    # Three upward-pointing V bands on the fuselage.  The broad lower edge is
    # orange, the face is amber, and the two-pixel upper rim is cream.
    for y in (258, 279, 300):
        path = [(170, y + 10), (256, y - 5), (342, y + 10)]
        lower = line_mask(path, 19)
        face = line_mask(path, 14)
        highlight = line_mask([(170, y + 5), (256, y - 10), (342, y + 5)], 2)
        out[lower & armour] = ORANGE
        out[face & armour] = AMBER
        out[highlight & armour] = CREAM

    # One smaller stripe per wing, aligned with each wing's leading edge.
    wing_paths = [
        [(68, 252), (151, 216)],
        [(361, 216), (444, 252)],
    ]
    for path in wing_paths:
        lower = line_mask(path, 16)
        face = line_mask(path, 12)
        highlight = line_mask([(path[0][0], path[0][1] - 3), (path[1][0], path[1][1] - 3)], 2)
        out[lower & armour] = ORANGE
        out[face & armour] = AMBER
        out[highlight & armour] = CREAM
    return out


def hit_flash_bone_repaint(cell: np.ndarray) -> np.ndarray:
    """Keep the flash bone-white while giving Storm a visible white ramp."""
    out = cell.copy()
    r, g, b = cell.astype(np.int16).transpose(2, 0, 1)
    bone = (
        (r > 145)
        & (g > 140)
        & (b > 125)
        & (r >= g - 4)
        & (g >= b - 4)
        & np.any(cell != GREEN, axis=2)
    )
    yy, xx = np.indices(bone.shape)
    # Leave the upper central glass/flash area untouched for canopy fidelity.
    canopy_keepout = (xx >= 145) & (xx < 367) & (yy >= 52) & (yy < 252)
    bone &= ~canopy_keepout
    lum = cell.astype(np.float32).mean(axis=2)
    out[bone & (lum >= 242)] = BONE_HOT
    out[bone & (lum >= 205) & (lum < 242)] = BONE_LIGHT
    out[bone & (lum >= 165) & (lum < 205)] = BONE_MID
    out[bone & (lum < 165)] = BONE_SHADOW
    return out


def remove_magenta_fringe(rgb: np.ndarray) -> np.ndarray:
    """Replace inherited purple fringe pixels with allowed ink/red ramps."""
    out = rgb.copy()
    hue, sat, value = hsv_image(rgb)
    bad = (
        (hue >= 285.0)
        & (hue < 345.0)
        & (sat > 0.25)
        & (value > 0.08)
        & np.any(rgb != GREEN, axis=2)
    )
    luminance = (
        rgb[..., 0].astype(np.float32) * 0.2126
        + rgb[..., 1].astype(np.float32) * 0.7152
        + rgb[..., 2].astype(np.float32) * 0.0722
    )
    out[bad & (luminance < 38.0)] = INK
    out[bad & (luminance >= 38.0) & (luminance < 105.0)] = RED_DETAIL_DARK
    out[bad & (luminance >= 105.0)] = RED_DETAIL_MID
    return out


def build_sheet(source: Path, target: Path) -> None:
    reference = np.array(Image.open(source).convert("RGB"), dtype=np.uint8)
    painted = reference.copy()
    for row in range(3):
        for col in range(3):
            y0, y1 = row * 512, (row + 1) * 512
            x0, x1 = col * 512, (col + 1) * 512
            cell = reference[y0:y1, x0:x1]
            # Cell 8 is the approved hit flash; do not introduce amber into it.
            if (row, col) == (2, 2):
                painted[y0:y1, x0:x1] = hit_flash_bone_repaint(cell)
            else:
                painted[y0:y1, x0:x1] = paint_storm_chevrons(cell)

    painted = remove_magenta_fringe(painted)
    enlarged = np.repeat(np.repeat(painted, 2, axis=0), 2, axis=1)
    Image.fromarray(enlarged, mode="RGB").save(target, optimize=True)


def main() -> None:
    for state in range(3):
        build_sheet(
            ROOT / f"skinref_state{state}.png",
            ROOT / f"skin_Storm_state{state}.png",
        )


if __name__ == "__main__":
    main()
