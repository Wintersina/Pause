#!/usr/bin/env python3
"""Build the reference-locked VoltViper Bone colourway.

The image-generation candidates established the Bone ceramic and electric-blue
emissive direction. This final paint pass keeps the approved flipbook as the
drawing authority: pose geometry, cell anchors, panel seams, canopy glass,
damage, cracks, glints, engine legs, and effects remain locked to the source.
The repaint uses explicit hard pixel ramps and a nearest-neighbour 2x export.
"""

from __future__ import annotations

import colorsys
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)

# Warm ceramic ramp, dark-to-light. The named base and shadow are the brief's
# anchors; the adjacent steps preserve the source's hard-band pixel texture.
BONE_DEEP = np.array([55, 50, 43], dtype=np.uint8)
BONE_SEAM = np.array([106, 99, 86], dtype=np.uint8)
BONE_SHADOW = np.array([185, 174, 152], dtype=np.uint8)  # #B9AE98
BONE_BASE = np.array([244, 234, 212], dtype=np.uint8)  # #F4EAD4
BONE_LIGHT = np.array([249, 244, 230], dtype=np.uint8)
BONE_HIGHLIGHT = np.array([255, 252, 241], dtype=np.uint8)
BONE_HOT = np.array([255, 255, 250], dtype=np.uint8)

# Electric-blue repaint for the wing bolts, wingtip diamond lights, and center
# star. Keep the exact requested base/core colours in the ramp.
BLUE_DEEP = np.array([20, 75, 126], dtype=np.uint8)
BLUE_BASE = np.array([46, 155, 230], dtype=np.uint8)  # #2E9BE6
BLUE_LIGHT = np.array([88, 181, 242], dtype=np.uint8)
BLUE_CORE = np.array([143, 211, 255], dtype=np.uint8)  # #8FD3FF
BLUE_HOT = np.array([232, 250, 255], dtype=np.uint8)


def hsv_image(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    flat = rgb.reshape(-1, 3).astype(np.float32) / 255.0
    hsv = np.array([colorsys.rgb_to_hsv(*pixel) for pixel in flat], dtype=np.float32)
    hsv = hsv.reshape(rgb.shape)
    return hsv[..., 0] * 360.0, hsv[..., 1], hsv[..., 2]


def source_luminance(rgb: np.ndarray) -> np.ndarray:
    work = rgb.astype(np.float32)
    return work[..., 0] * 0.2126 + work[..., 1] * 0.7152 + work[..., 2] * 0.0722


def blue_armour_mask(rgb: np.ndarray) -> np.ndarray:
    """Select blue diffuse armour without touching the teal canopy."""
    hue, sat, value = hsv_image(rgb)
    # Include the source's cyan-blue edge/rim tones as armour too. The central
    # canopy keepout below is what protects the teal glass and its glints.
    mask = (hue >= 180.0) & (hue <= 285.0) & (sat >= 0.22) & (value >= 0.08)

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


def paint_bone_armour(rgb: np.ndarray, mask: np.ndarray) -> np.ndarray:
    out = rgb.copy()
    lum = source_luminance(rgb)
    out[mask & (lum < 30)] = BONE_DEEP
    out[mask & (lum >= 30) & (lum < 58)] = BONE_SEAM
    out[mask & (lum >= 58) & (lum < 96)] = BONE_SHADOW
    out[mask & (lum >= 96) & (lum < 138)] = BONE_BASE
    out[mask & (lum >= 138) & (lum < 180)] = BONE_LIGHT
    out[mask & (lum >= 180) & (lum < 224)] = BONE_HIGHLIGHT
    out[mask & (lum >= 224)] = BONE_HOT
    return out


def accent_candidates(rgb: np.ndarray) -> np.ndarray:
    """Find the original cream/gold emissive details by fixed cell regions."""
    hue, sat, value = hsv_image(rgb)
    lum = source_luminance(rgb)
    yy, xx = np.indices(rgb.shape[:2])
    local_x = xx % 512
    local_y = yy % 512

    # Cream wing-bolt lights and the pale interiors of the wingtip diamonds.
    wing_region = (
        (((local_x >= 22) & (local_x < 190)) | ((local_x >= 322) & (local_x < 490)))
        & (local_y >= 170)
        & (local_y < 340)
    )
    pale = (sat < 0.30) & (value > 0.42) & (lum > 92)
    wing_lights = wing_region & pale

    # The central star is gold/orange in the approved blue reference. It is a
    # dedicated fixed cell feature, so repaint it without touching surrounding
    # orange damage FX.
    star_region = (
        (local_x >= 204)
        & (local_x < 308)
        & (local_y >= 275)
        & (local_y < 375)
    )
    star = star_region & (value > 0.16) & (lum > 28) & ~((hue >= 150.0) & (hue < 285.0))
    star &= ~((sat > 0.78) & (hue >= 285.0) & (hue < 345.0))
    return wing_lights | star


def paint_electric_blue(rgb: np.ndarray, mask: np.ndarray) -> np.ndarray:
    out = rgb.copy()
    lum = source_luminance(rgb)
    out[mask & (lum < 70)] = BLUE_DEEP
    out[mask & (lum >= 70) & (lum < 130)] = BLUE_BASE
    out[mask & (lum >= 130) & (lum < 185)] = BLUE_LIGHT
    out[mask & (lum >= 185) & (lum < 232)] = BLUE_CORE
    out[mask & (lum >= 232)] = BLUE_HOT
    return out


def bone_hit_flash(rgb: np.ndarray) -> np.ndarray:
    """Warm the near-white hit cell without changing its bone-white read."""
    out = rgb.copy()
    hue, sat, value = hsv_image(rgb)
    lum = source_luminance(rgb)
    mask = (sat < 0.28) & (value > 0.16) & np.any(rgb != GREEN, axis=2)
    out[mask & (lum < 80)] = BONE_SEAM
    out[mask & (lum >= 80) & (lum < 155)] = BONE_SHADOW
    out[mask & (lum >= 155) & (lum < 210)] = BONE_BASE
    out[mask & (lum >= 210) & (lum < 242)] = BONE_LIGHT
    out[mask & (lum >= 242)] = BONE_HOT
    return out


def remove_magenta_fringe(rgb: np.ndarray) -> np.ndarray:
    """Keep the no-pink rule without changing the approved ship geometry."""
    out = rgb.copy()
    hue, sat, value = hsv_image(rgb)
    bad = (
        (hue >= 285.0)
        & (hue < 345.0)
        & (sat > 0.25)
        & (value > 0.08)
        & np.any(rgb != GREEN, axis=2)
    )
    lum = source_luminance(rgb)
    out[bad & (lum < 100)] = np.array([72, 27, 24], dtype=np.uint8)
    out[bad & (lum >= 100)] = np.array([170, 38, 34], dtype=np.uint8)
    return out


def build_sheet(source: Path, target: Path) -> None:
    reference = np.array(Image.open(source).convert("RGB"), dtype=np.uint8)
    painted = paint_bone_armour(reference, blue_armour_mask(reference))

    # Cell (2, 2) is the approved hit flash. Its whole diffuse material stays
    # bone-white, while the other eight cells receive the blue emissive accents.
    flash = np.zeros(reference.shape[:2], dtype=bool)
    flash[2 * 512 : 3 * 512, 2 * 512 : 3 * 512] = True
    accents = accent_candidates(reference) & ~flash
    painted = paint_electric_blue(painted, accents)
    painted[2 * 512 : 3 * 512, 2 * 512 : 3 * 512] = bone_hit_flash(
        reference[2 * 512 : 3 * 512, 2 * 512 : 3 * 512]
    )
    painted = remove_magenta_fringe(painted)

    enlarged = np.repeat(np.repeat(painted, 2, axis=0), 2, axis=1)
    Image.fromarray(enlarged, mode="RGB").save(target, optimize=True)


def main() -> None:
    for state in range(3):
        build_sheet(
            ROOT / f"skinref_state{state}.png",
            ROOT / f"skin_Bone_state{state}.png",
        )


if __name__ == "__main__":
    main()
