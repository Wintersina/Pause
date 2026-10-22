#!/usr/bin/env python3
"""Build the Flint chevron repaint from the approved Ligher sheets.

The approved sheets remain the geometry authority.  This pass adds the Flint
bone-chevron livery at native resolution, protects the canopy/lights, leaves
state-specific damage in place, removes forbidden magenta damage tint, and
exports each sheet with exact 2x nearest-neighbour pixels.
"""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)
BONE = np.array([0xF4, 0xEA, 0xD4], dtype=np.uint8)
BONE_SHADOW = np.array([0xB9, 0xAE, 0x98], dtype=np.uint8)
BONE_HIGHLIGHT = np.array([0xFF, 0xFA, 0xE8], dtype=np.uint8)

# Flint keeps the ship's copper identity, but uses a darker, cooler copper
# ramp with fresh hard bands and plate-edge highlights.  This is deliberately
# a material repaint rather than a channel rotation.
FLINT_COPPER_RAMP = np.array(
    [
        [0x32, 0x1D, 0x19],
        [0x58, 0x2D, 0x20],
        [0x7E, 0x3E, 0x22],
        [0xA9, 0x56, 0x28],
        [0xD0, 0x78, 0x38],
        [0xF0, 0xA0, 0x5A],
        [0xFF, 0xD0, 0x8A],
    ],
    dtype=np.uint8,
)

# The white hit-flash is retained as a bone ceramic ramp rather than being
# recoloured into copper.  This changes only neutral hull pixels, never glass
# or emissive effects.
FLASH_RAMP = np.array(
    [
        [0x6D, 0x66, 0x5C],
        [0x94, 0x8A, 0x7A],
        [0xB9, 0xAE, 0x98],
        [0xD0, 0xC5, 0xB1],
        [0xF4, 0xEA, 0xD4],
        [0xFF, 0xFA, 0xE8],
    ],
    dtype=np.uint8,
)


def hsv(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    f = rgb.astype(np.float32) / 255.0
    r, g, b = f[..., 0], f[..., 1], f[..., 2]
    mx = f.max(axis=-1)
    mn = f.min(axis=-1)
    d = mx - mn
    h = np.zeros_like(mx)
    nz = d > 1e-6
    rr = ((g - b) / np.maximum(d, 1e-6)) % 6.0
    gg = ((b - r) / np.maximum(d, 1e-6)) + 2.0
    bb = ((r - g) / np.maximum(d, 1e-6)) + 4.0
    h[nz & (mx == r)] = rr[nz & (mx == r)]
    h[nz & (mx == g)] = gg[nz & (mx == g)]
    h[nz & (mx == b)] = bb[nz & (mx == b)]
    h /= 6.0
    s = np.divide(d, np.maximum(mx, 1e-6), out=np.zeros_like(mx), where=mx > 1e-6)
    return h, s, mx


def blue_region(cell: np.ndarray) -> np.ndarray:
    r, g, b = [cell[..., i].astype(np.int16) for i in range(3)]
    return (g > 50) & (b > 50) & (b > r * 1.10) & (g > r * 1.10)


def protected_effects(cell: np.ndarray) -> np.ndarray:
    """Protect canopy, slot/fin lights, and the amber nozzle from repaint."""
    r, g, b = [cell[..., i].astype(np.float32) for i in range(3)]
    maxc = np.maximum.reduce([r, g, b])
    minc = np.minimum.reduce([r, g, b])
    saturation = (maxc - minc) / np.maximum(maxc, 1.0)
    red_light = (r > 190) & (r > g * 2.4) & (r > b * 2.4)
    yy, xx = np.indices(r.shape)
    nozzle = (
        (yy >= 425)
        & (np.abs(xx - 256) <= 58)
        & (maxc > 115)
        & (g > r * 0.38)
        & (b > g * 0.14)
    )
    return blue_region(cell) | red_light | nozzle | ((saturation < 0.18) & (maxc > 170))


def chevron_mask(index: int) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Return hard-edged native masks for base, right-edge shadow, highlight."""
    # Native reference pixels are 512; the requested 22 px bars at 1024
    # export scale are 11 native pixels.  Banked poses foreshorten and shift
    # the centreline with the existing hull.
    centres = [256, 256, 256, 256, 256, 256, 246, 253, 256]
    centre_x = centres[index]
    half_widths = [38, 34, 30]
    apex_y = [337, 365, 393]
    arm_lengths = [29, 27, 25]
    h = w = 512
    base_img = Image.new("L", (w, h), 0)
    draw = ImageDraw.Draw(base_img)
    for half_width, apex, length in zip(half_widths, apex_y, arm_lengths):
        left = (centre_x - half_width, apex + length)
        tip = (centre_x, apex)
        right = (centre_x + half_width, apex + length)
        draw.line([left, tip], fill=255, width=11)
        draw.line([tip, right], fill=255, width=11)
    base = np.asarray(base_img, dtype=bool)

    # A compact lower-right offset supplies the requested Flint edge shadow;
    # a top-left one-pixel glint keeps the decal crisp without soft AA.
    shadow = np.zeros_like(base)
    shadow[1:, 2:] = base[:-1, :-2]
    shadow &= base
    highlight = np.zeros_like(base)
    highlight[:-1, :-1] = base[1:, 1:]
    highlight = base & ~highlight & ~shadow
    return base, shadow, highlight


def paint_chevrons(cell: np.ndarray, base_cell: np.ndarray, state: int, index: int) -> np.ndarray:
    out = cell.copy()
    non_green = np.any(cell != GREEN, axis=2)
    allowed = non_green & ~protected_effects(cell)

    # In damaged/wreck sheets, only repaint pixels that still match the
    # corresponding intact pose.  Damage, sparks, soot and exposed machinery
    # therefore cut through the chevrons instead of being covered by them.
    if state != 0 and index != 8:
        allowed &= np.all(cell == base_cell, axis=2)

    base, shadow, highlight = chevron_mask(index)
    target = allowed & base
    out[target] = BONE
    out[target & shadow] = BONE_SHADOW
    out[target & highlight] = BONE_HIGHLIGHT
    return out


def repaint_copper(cell: np.ndarray, base_cell: np.ndarray, state: int, index: int) -> np.ndarray:
    """Re-shade unchanged copper plates with the Flint copper ramp."""
    out = cell.copy()
    non_green = np.any(cell != GREEN, axis=2)
    r, g, b = [cell[..., i].astype(np.float32) for i in range(3)]
    copper = (
        non_green
        & (r > g * 1.22)
        & (g > b * 1.18)
        & (g >= 1.0)
        & (r >= 20.0)
        & ~protected_effects(cell)
    )
    if state != 0 and index != 8:
        copper &= np.all(cell == base_cell, axis=2)

    luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0
    tone = np.select(
        [luminance < 0.16, luminance < 0.28, luminance < 0.42,
         luminance < 0.58, luminance < 0.76, luminance < 0.90],
        [0, 1, 2, 3, 4, 5],
        default=6,
    )
    out[copper] = FLINT_COPPER_RAMP[tone[copper]]

    # Put a few Flint-hot pixels on upper-left plate edges, using the same
    # existing pixel geometry to derive an edge rather than drawing new shapes.
    shifted = np.pad(luminance, ((0, 1), (0, 1)), mode="edge")[1:, 1:]
    rim = copper & (tone >= 3) & ((luminance - shifted) > 0.045)
    out[rim] = FLINT_COPPER_RAMP[5]
    sparkle = copper & (tone >= 5) & (((np.indices(copper.shape)[1] * 17
                                        + np.indices(copper.shape)[0] * 29
                                        + state * 11) % 173) == 0)
    out[sparkle] = FLINT_COPPER_RAMP[6]
    return out


def repaint_hit_flash(cell: np.ndarray) -> np.ndarray:
    out = cell.copy()
    h, s, value = hsv(cell)
    neutral = (
        np.any(cell != GREEN, axis=2)
        & ~blue_region(cell)
        & (s < 0.25)
        & (value > 0.05)
    )
    level = np.select(
        [value < 0.25, value < 0.42, value < 0.60, value < 0.78, value < 0.92],
        [0, 1, 2, 3, 4],
        default=5,
    )
    out[neutral] = FLASH_RAMP[level[neutral]]
    return out


def remove_forbidden_magenta(image: np.ndarray) -> None:
    """Move legacy pink/magenta damage tint to Flint-safe warm soot."""
    h, s, value = hsv(image)
    magenta = (s > 0.35) & (h > 0.82) & (h < 0.98) & (value > 0.18)
    image[magenta] = np.array([0x6A, 0x24, 0x04], dtype=np.uint8)


def remove_ship_green(image: np.ndarray) -> None:
    """Keep pure key green only as the background colour."""
    r, g, b = [image[..., i].astype(np.int16) for i in range(3)]
    non_green = np.any(image != GREEN, axis=2)
    green_tint = non_green & (g > r * 1.40) & (g > b * 1.40)
    image[green_tint] = FLINT_COPPER_RAMP[0]


def build_sheet(state: int) -> Path:
    ref = np.asarray(Image.open(ROOT / f"skinref_state{state}.png").convert("RGB"))
    intact = np.asarray(Image.open(ROOT / "skinref_state0.png").convert("RGB"))
    out = ref.copy()

    for index in range(9):
        y0 = (index // 3) * 512
        x0 = (index % 3) * 512
        cell = ref[y0:y0 + 512, x0:x0 + 512]
        base_cell = intact[y0:y0 + 512, x0:x0 + 512]
        painted = repaint_hit_flash(cell) if index == 8 else repaint_copper(
            cell, base_cell, state, index
        )
        painted = paint_chevrons(painted, base_cell, state, index)
        out[y0:y0 + 512, x0:x0 + 512] = painted

    remove_forbidden_magenta(out)
    remove_ship_green(out)
    final = Image.fromarray(out, mode="RGB").resize((3072, 3072), Image.Resampling.NEAREST)
    output = ROOT / f"skin_Flint_state{state}.png"
    final.save(output, format="PNG", optimize=False)
    return output


def main() -> None:
    for state in range(3):
        print(build_sheet(state))


if __name__ == "__main__":
    main()
