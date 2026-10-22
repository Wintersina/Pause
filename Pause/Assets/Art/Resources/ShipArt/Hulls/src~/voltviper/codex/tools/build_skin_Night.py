#!/usr/bin/env python3
"""Build the reference-locked VoltViper Night colourway sheets.

The approved sheets are the drawing authority.  The generated repaint candidates
were used to establish the material direction, while this pass keeps the approved
pixel drawing, cell anchors, damage, and canopy pixels exact and paints the armour
through a discrete midnight-navy ramp before the 2x nearest-neighbour export.
"""

from __future__ import annotations

from pathlib import Path

import colorsys
import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)

# The material ramp is deliberately explicit rather than a hue rotation.  The
# brightest step is the requested cyan edge light and is only reached by bright
# source plate pixels, retaining the existing hand-placed texture locations.
NIGHT_DEEP = np.array([17, 20, 46], dtype=np.uint8)
NIGHT_SHADOW = np.array([26, 31, 69], dtype=np.uint8)  # #1A1F45
NIGHT_BASE = np.array([42, 46, 107], dtype=np.uint8)  # #2A2E6B
NIGHT_MID = np.array([55, 62, 128], dtype=np.uint8)
NIGHT_LIGHT = np.array([78, 96, 153], dtype=np.uint8)
NIGHT_RIM = np.array([110, 242, 238], dtype=np.uint8)  # #6EF2EE
NIGHT_HOT = np.array([225, 255, 255], dtype=np.uint8)

# Bone-white retint for the hit-flash cell.  It remains bone-white, but carries
# enough value separation to make the finished colourway visibly distinct.
BONE_HOT = np.array([255, 255, 247], dtype=np.uint8)
BONE_LIGHT = np.array([247, 245, 235], dtype=np.uint8)
BONE_MID = np.array([233, 231, 220], dtype=np.uint8)
BONE_SHADOW = np.array([210, 208, 199], dtype=np.uint8)


def hsv_image(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    flat = rgb.reshape(-1, 3).astype(np.float32) / 255.0
    hsv = np.array([colorsys.rgb_to_hsv(*pixel) for pixel in flat], dtype=np.float32)
    hsv = hsv.reshape(rgb.shape)
    return hsv[..., 0] * 360.0, hsv[..., 1], hsv[..., 2]


def blue_armour_mask(rgb: np.ndarray) -> np.ndarray:
    hue, sat, value = hsv_image(rgb)
    # Stock blue plating is in the blue-violet range.  The teal canopy is kept
    # out by its lower hue range and by a conservative central canopy keepout.
    mask = (hue >= 198.0) & (hue <= 285.0) & (sat >= 0.22) & (value >= 0.08)
    height, width = mask.shape[:2]
    yy, xx = np.indices(mask.shape)
    local_x = xx % 512
    local_y = yy % 512
    # Keep every teal/canopy pixel in the same semantic region used by the
    # validator, including the narrow blue-violet edge pixels that blend into
    # the glass in the damaged and glint poses.
    canopy_keepout = (
        (local_x >= 165)
        & (local_x < 350)
        & (local_y >= 70)
        & (local_y < 245)
        & (hue >= 155.0)
        & (hue < 202.0)
        & (sat >= 0.18)
        & (value > 0.12)
    )
    return mask & ~canopy_keepout & np.any(rgb != GREEN, axis=2)


def paint_armour(rgb: np.ndarray, mask: np.ndarray) -> np.ndarray:
    out = rgb.copy()
    work = rgb.astype(np.float32)
    luminance = (
        work[..., 0] * 0.2126 + work[..., 1] * 0.7152 + work[..., 2] * 0.0722
    )

    # Hard ramp bands preserve the source's texture as material planes rather
    # than applying a continuous hue shift.
    out[mask & (luminance < 38)] = NIGHT_DEEP
    out[mask & (luminance >= 38) & (luminance < 72)] = NIGHT_SHADOW
    out[mask & (luminance >= 72) & (luminance < 112)] = NIGHT_BASE
    out[mask & (luminance >= 112) & (luminance < 148)] = NIGHT_MID
    out[mask & (luminance >= 148) & (luminance < 190)] = NIGHT_LIGHT
    out[mask & (luminance >= 190) & (luminance < 225)] = NIGHT_RIM
    out[mask & (luminance >= 225)] = NIGHT_HOT
    return out


def bone_mask(rgb: np.ndarray) -> np.ndarray:
    r, g, b = rgb.astype(np.int16).transpose(2, 0, 1)
    return (r > 150) & (g > 145) & (b > 130) & (r >= g) & (g >= b)


def paint_hit_flash(rgb: np.ndarray) -> np.ndarray:
    out = rgb.copy()
    lum = rgb.astype(np.float32).mean(axis=2)
    mask = bone_mask(rgb)
    out[mask & (lum >= 245)] = BONE_HOT
    out[mask & (lum >= 220) & (lum < 245)] = BONE_LIGHT
    out[mask & (lum >= 185) & (lum < 220)] = BONE_MID
    out[mask & (lum < 185)] = BONE_SHADOW
    return out


def build_sheet(source: Path, target: Path) -> None:
    reference = np.array(Image.open(source).convert("RGB"), dtype=np.uint8)
    painted = paint_armour(reference, blue_armour_mask(reference))

    # Cell (2, 2) is the approved hit flash.  Keep it bone-white instead of
    # converting it to navy, but retain a controlled bone ramp for the colourway.
    y0, y1 = 2 * 512, 3 * 512
    x0, x1 = 2 * 512, 3 * 512
    painted[y0:y1, x0:x1] = paint_hit_flash(reference[y0:y1, x0:x1])

    # The source sheet is the native 512px-cell drawing.  Export at the required
    # 1024px-cell size without introducing anti-aliasing or filtered pixels.
    enlarged = np.repeat(np.repeat(painted, 2, axis=0), 2, axis=1)
    Image.fromarray(enlarged, mode="RGB").save(target, optimize=True)


def main() -> None:
    for state in range(3):
        build_sheet(
            ROOT / f"skinref_state{state}.png",
            ROOT / f"skin_Night_state{state}.png",
        )


if __name__ == "__main__":
    main()
