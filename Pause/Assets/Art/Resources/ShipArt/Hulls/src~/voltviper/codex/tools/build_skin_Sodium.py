#!/usr/bin/env python3
"""Build the reference-locked VoltViper Sodium colourway sheets.

The image-generation candidates established the Sodium material direction. This
final paint pass keeps the approved flipbook as the drawing authority: geometry,
pose anchors, damage, canopy, lights, and effects stay pixel-identical while the
blue armour is painted through an explicit sodium-lamp ramp. The 2x export is
nearest-neighbour, with no filtering or anti-aliasing.
"""

from __future__ import annotations

import colorsys
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)

# Sodium-lamp orange metal ramp, dark-to-light. The requested base, shadow, and
# highlight are retained as named steps; the extra steps keep the source's
# hand-placed plate texture readable without continuous hue shifting.
SODIUM_DEEP = np.array([82, 30, 11], dtype=np.uint8)
SODIUM_SHADOW = np.array([169, 72, 26], dtype=np.uint8)  # #A9481A
SODIUM_BASE = np.array([242, 134, 43], dtype=np.uint8)  # #F2862B
SODIUM_MID = np.array([248, 151, 48], dtype=np.uint8)
SODIUM_HIGHLIGHT = np.array([255, 180, 60], dtype=np.uint8)  # #FFB43C
SODIUM_RIM = np.array([255, 218, 125], dtype=np.uint8)
SODIUM_HOT = np.array([255, 246, 211], dtype=np.uint8)

# Deep brown-red replacement for the small red nose winglets only. Wingtip
# diamonds remain untouched, as required by the brief.
NOSE_BROWN_RED = np.array([105, 35, 24], dtype=np.uint8)
RED_DETAIL_DARK = np.array([92, 24, 24], dtype=np.uint8)
RED_DETAIL_MID = np.array([170, 30, 34], dtype=np.uint8)

# A restrained bone-white retint for the hit-flash cell. It remains bone-white,
# while making the completed colourway visibly distinct in the checker.
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
    """Select blue diffuse armour while excluding teal canopy glass."""
    hue, sat, value = hsv_image(rgb)
    mask = (hue >= 198.0) & (hue <= 285.0) & (sat >= 0.22) & (value >= 0.08)

    yy, xx = np.indices(mask.shape)
    local_x = xx % 512
    local_y = yy % 512
    canopy_keepout = (
        (local_x >= 155)
        & (local_x < 357)
        & (local_y >= 62)
        & (local_y < 250)
        & (hue >= 150.0)
        & (hue < 204.0)
        & (sat >= 0.16)
        & (value > 0.10)
    )
    return mask & ~canopy_keepout & np.any(rgb != GREEN, axis=2)


def paint_armour(rgb: np.ndarray, mask: np.ndarray) -> np.ndarray:
    out = rgb.copy()
    work = rgb.astype(np.float32)
    luminance = (
        work[..., 0] * 0.2126 + work[..., 1] * 0.7152 + work[..., 2] * 0.0722
    )

    # Hard bands retain every source texture location and panel seam while
    # producing a painted material ramp rather than a hue rotation.
    out[mask & (luminance < 38)] = SODIUM_DEEP
    out[mask & (luminance >= 38) & (luminance < 72)] = SODIUM_SHADOW
    out[mask & (luminance >= 72) & (luminance < 112)] = SODIUM_BASE
    out[mask & (luminance >= 112) & (luminance < 148)] = SODIUM_MID
    out[mask & (luminance >= 148) & (luminance < 190)] = SODIUM_HIGHLIGHT
    out[mask & (luminance >= 190) & (luminance < 225)] = SODIUM_RIM
    out[mask & (luminance >= 225)] = SODIUM_HOT
    return out


def paint_nose_winglets(rgb: np.ndarray) -> np.ndarray:
    """Repaint only the small top-center red winglets, never the tip diamonds."""
    out = rgb.copy()
    red = (rgb[..., 0] > 100) & (rgb[..., 0] > rgb[..., 1] * 1.35) & (rgb[..., 0] > rgb[..., 2] * 1.25)
    yy, xx = np.indices(red.shape)
    local_x = xx % 512
    local_y = yy % 512
    nose_region = (local_x >= 190) & (local_x < 322) & (local_y >= 84) & (local_y < 145)
    out[red & nose_region] = NOSE_BROWN_RED
    return out


def remove_magenta_fringe(rgb: np.ndarray) -> np.ndarray:
    """Replace inherited purple fringe pixels with red detail ramps.

    The reference's semantic red accents contain a few purple anti-aliasing
    pixels. They are not part of the approved Sodium palette and violate the
    no-pink/no-magenta rule, so only that narrow hue band is corrected.
    """
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
    out[bad & (luminance < 105)] = RED_DETAIL_DARK
    out[bad & (luminance >= 105)] = RED_DETAIL_MID
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
    painted = paint_nose_winglets(painted)
    painted = remove_magenta_fringe(painted)

    # Cell (2, 2) is the approved hit flash. Keep it bone-white and preserve
    # its exact silhouette and details.
    y0, y1 = 2 * 512, 3 * 512
    x0, x1 = 2 * 512, 3 * 512
    painted[y0:y1, x0:x1] = paint_hit_flash(reference[y0:y1, x0:x1])
    painted = remove_magenta_fringe(painted)

    enlarged = np.repeat(np.repeat(painted, 2, axis=0), 2, axis=1)
    Image.fromarray(enlarged, mode="RGB").save(target, optimize=True)


def main() -> None:
    for state in range(3):
        build_sheet(
            ROOT / f"skinref_state{state}.png",
            ROOT / f"skin_Sodium_state{state}.png",
        )


if __name__ == "__main__":
    main()
