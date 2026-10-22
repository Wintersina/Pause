#!/usr/bin/env python3
"""Build the Amber Ligher sheets from the approved keyed references.

The image-generation candidates establish the Amber material language, but the
shipping repaint is deterministic: the approved reference supplies geometry,
damage, lighting effects, and all non-armour pixels. Only the copper armour
pixels are remapped to a new hard-banded Amber metal ramp, then the result is
upscaled 2x with nearest-neighbour.
"""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)

# Amber material ramp, shadow -> highlight. These are the requested anchors;
# the intermediate bands keep the paint hard-edged and pixel-faithful.
AMBER_RAMP = np.array(
    [
        [169, 72, 26],    # #A9481A deepest seams / shadow
        [196, 87, 27],
        [242, 134, 43],   # #F2862B shadow
        [255, 180, 60],   # #FFB43C base
        [255, 205, 112],
        [244, 234, 212],  # #F4EAD4 cream-hot edge highlight
    ],
    dtype=np.uint8,
)

# Bone-white hit flash: keep the cell bone-white while allowing a fresh clean
# ramp instead of tinting it amber.
BONE_RAMP = np.array(
    [
        [164, 158, 151],
        [205, 199, 188],
        [232, 226, 214],
        [244, 234, 212],
        [254, 249, 231],
        [255, 250, 232],
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


def source_copper_mask(cell: np.ndarray, base_cell: np.ndarray, index: int) -> np.ndarray:
    """Find only unchanged warm armour, excluding lights, glass, and effects."""
    non_green = np.any(cell != GREEN, axis=2)
    unchanged_base = np.all(cell == base_cell, axis=2)
    eligible = non_green & unchanged_base & (index != 8)

    # Copper has a warm red-over-green-over-blue ordering. This excludes the
    # teal canopy, red emissive lights, neutral sockets, and black outline.
    r = cell[..., 0].astype(np.float32)
    g = cell[..., 1].astype(np.float32)
    b = cell[..., 2].astype(np.float32)
    copper = eligible & (r > g * 1.25) & (g > b * 1.35) & (g >= 1.0) & (r >= 20.0)

    # The bottom-center nozzle is an emissive effect, not armour. Its source
    # hue overlaps the warm metal ramp, so protect it by its fixed cell-local
    # position and bright emissive values.
    _, _, value = hsv(cell)
    yy, xx = np.indices(copper.shape)
    nozzle = (
        copper
        & (yy >= 425)
        & (np.abs(xx - 256) <= 55)
        & (value >= 0.55)
        & (g > r * 0.45)
        & (b > g * 0.20)
    )
    return copper & ~nozzle


def repaint_armour(cell: np.ndarray, base_cell: np.ndarray, index: int, state: int) -> np.ndarray:
    out = cell.copy()
    copper = source_copper_mask(cell, base_cell, index)
    luminance = (0.299 * cell[..., 0] + 0.587 * cell[..., 1] + 0.114 * cell[..., 2]) / 255.0

    tone = np.select(
        [luminance < 0.20, luminance < 0.34, luminance < 0.50,
         luminance < 0.64, luminance < 0.79],
        [0, 1, 2, 3, 4],
        default=5,
    )

    # Fresh top-left metal edge lighting: bright source pixels whose lower-right
    # neighbour is darker are promoted to the cream-hot Amber edge band.
    shifted = np.pad(luminance, ((0, 1), (0, 1)), mode="edge")[1:, 1:]
    rim = copper & (tone >= 3) & ((luminance - shifted) > 0.045)
    out[copper] = AMBER_RAMP[tone[copper]]
    out[rim] = AMBER_RAMP[5]

    # Sparse hot plate-edge flecks make this a material repaint with a distinct
    # Amber ramp while staying inside existing armour pixels.
    yy, xx = np.indices(copper.shape)
    sparkle = copper & (tone >= 4) & (((xx * 17 + yy * 29 + state * 11) % 137) == 0)
    out[sparkle] = AMBER_RAMP[5]
    return out


def repaint_hit_flash(cell: np.ndarray) -> np.ndarray:
    out = cell.copy()
    _, saturation, value = hsv(cell)
    neutral = (saturation < 0.20) & (value > 0.45) & np.any(cell != GREEN, axis=2)
    level = np.select(
        [value < 0.68, value < 0.82, value < 0.92, value < 0.97, value < 0.995],
        [0, 1, 2, 3, 4],
        default=5,
    )
    out[neutral] = BONE_RAMP[level[neutral]]
    return out


def remove_forbidden_magenta(image: np.ndarray) -> None:
    """Keep the approved damage pattern, but move legacy magenta pixels to amber soot."""
    h, s, _ = hsv(image)
    r = image[..., 0].astype(np.float32) / 255.0
    b = image[..., 2].astype(np.float32) / 255.0
    magenta = (s > 0.50) & (h > 0.83) & (h < 0.97) & (r > 0.25) & (b > 0.25)
    image[magenta] = np.array([106, 36, 4], dtype=np.uint8)


def build_sheet(state: int) -> Path:
    ref = np.asarray(Image.open(ROOT / f"skinref_state{state}.png").convert("RGB"))
    intact = np.asarray(Image.open(ROOT / "skinref_state0.png").convert("RGB"))
    out = np.empty_like(ref)

    for index in range(9):
        y0 = (index // 3) * 512
        x0 = (index % 3) * 512
        cell = ref[y0:y0 + 512, x0:x0 + 512]
        base = intact[y0:y0 + 512, x0:x0 + 512]
        if index == 8:
            painted = repaint_hit_flash(cell)
        else:
            painted = repaint_armour(cell, base, index, state)
        out[y0:y0 + 512, x0:x0 + 512] = painted

    remove_forbidden_magenta(out)
    image = Image.fromarray(out, mode="RGB").resize((3072, 3072), Image.Resampling.NEAREST)
    output = ROOT / f"skin_Amber_state{state}.png"
    image.save(output, format="PNG", optimize=False)
    return output


def main() -> None:
    for state in range(3):
        print(build_sheet(state))


if __name__ == "__main__":
    main()
