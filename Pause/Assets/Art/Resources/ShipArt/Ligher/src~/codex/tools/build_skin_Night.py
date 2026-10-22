#!/usr/bin/env python3
"""Build the Night repaint from the approved keyed reference sheets.

The image-generation candidates supplied the material direction.  This pass is
deliberately deterministic: it keeps the approved drawing as the geometry
source, preserves state-specific damage pixels, paints the unchanged copper
material with a fresh navy/cyan ramp, and performs the requested 2x nearest
neighbour export.
"""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)

# The ramp is intentionally hard-banded rather than a hue rotation.  The first
# two colors are the Night brief's required shadow/base anchors.
NIGHT_RAMP = np.array(
    [
        [26, 31, 69],    # #1A1F45 shadow
        [32, 38, 83],
        [42, 46, 107],   # #2A2E6B base
        [61, 78, 148],
        [78, 111, 190],
        [110, 242, 238], # #6EF2EE rim light
        [218, 255, 255], # pale blue-white sparkle
    ],
    dtype=np.uint8,
)

# A neutral bone ramp keeps the hit-flash cell bone-white while still making a
# real, readable material repaint in that cell.
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
    """Vectorized RGB->HSV with hue in [0, 1)."""
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


def repaint_copper(cell: np.ndarray, base_cell: np.ndarray, cell_index: int, state: int) -> np.ndarray:
    out = cell.copy()
    non_green = np.any(cell != GREEN, axis=2)

    # The approved state sheets share their exact base drawing.  In the damaged
    # and wreck sheets, pixels that differ from intact are effects/exposed
    # machinery and must remain verbatim.
    unchanged_base = np.all(cell == base_cell, axis=2)
    eligible = non_green & unchanged_base & (cell_index != 8)

    h, s, v = hsv(cell)
    # Warm copper is identified by its orange channel ordering, including the
    # near-black copper shadows whose HSV hue wraps toward red.  Requiring the
    # green channel to exceed blue keeps the red slot/diamond lights intact;
    # the cool canopy is excluded by both hue and channel ordering.
    r = cell[..., 0].astype(np.float32)
    g = cell[..., 1].astype(np.float32)
    b = cell[..., 2].astype(np.float32)
    copper = eligible & (r > g * 1.25) & (g > b * 1.35) & (g >= 1.0) & (r >= 20.0)

    # Keep the small amber nozzle glow exactly as the reference, even where its
    # hue overlaps the copper ramp.
    yy, xx = np.indices(copper.shape)
    nozzle = (
        copper
        & (yy >= 425)
        & (np.abs(xx - 256) <= 55)
        & (v >= 0.55)
        & (g > r * 0.45)
        & (b > g * 0.20)
    )
    copper &= ~nozzle

    # Hard material bands from source value, with a directional light test for
    # fresh upper-left cyan rim pixels.  This is a material repaint, not RGB
    # channel rotation.
    luminance = (0.299 * cell[..., 0] + 0.587 * cell[..., 1] + 0.114 * cell[..., 2]) / 255.0
    tone = np.select(
        [luminance < 0.20, luminance < 0.34, luminance < 0.50,
         luminance < 0.64, luminance < 0.79],
        [0, 1, 2, 3, 4],
        default=5,
    )

    # Bright faces whose lower-right neighbour is darker become the cyan edge
    # lighting characteristic of the selected Night candidate.
    shifted = np.pad(luminance, ((0, 1), (0, 1)), mode="edge")[1:, 1:]
    rim = copper & (tone >= 3) & ((luminance - shifted) > 0.045)
    out[copper] = NIGHT_RAMP[tone[copper]]
    out[rim] = NIGHT_RAMP[5]

    # A few deterministic pale sparkles stay inside the painted armour and do
    # not alter the silhouette or any reference light effect.
    sparkle = copper & (tone >= 4) & (((xx * 17 + yy * 29 + state * 11) % 137) == 0)
    out[sparkle] = NIGHT_RAMP[6]
    return out


def repaint_hit_flash(cell: np.ndarray) -> np.ndarray:
    """Retint only the bone ramp; the hit-flash remains visibly bone-white."""
    out = cell.copy()
    h, s, v = hsv(cell)
    neutral = (s < 0.20) & (v > 0.45) & np.any(cell != GREEN, axis=2)
    # Keep outlines/deep sockets intact; quantize pale surfaces to a clean bone
    # ramp so the cell remains bone-white rather than becoming Night navy.
    level = np.select(
        [v < 0.68, v < 0.82, v < 0.92, v < 0.97, v < 0.995],
        [0, 1, 2, 3, 4],
        default=5,
    )
    out[neutral] = BONE_RAMP[level[neutral]]
    return out


def remove_magenta(image: np.ndarray) -> np.ndarray:
    """Replace inherited purple damage pixels with dark orange damage tones."""
    h, s, v = hsv(image)
    r = image[..., 0].astype(np.float32) / 255.0
    b = image[..., 2].astype(np.float32) / 255.0
    magenta = (s > 0.50) & (h > 0.83) & (h < 0.97) & (r > 0.25) & (b > 0.25)
    if magenta.any():
        image[magenta] = np.array([106, 36, 4], dtype=np.uint8)  # #6A2404
    return image


def build_sheet(state: int) -> Path:
    ref_path = ROOT / f"skinref_state{state}.png"
    intact_path = ROOT / "skinref_state0.png"
    ref = np.asarray(Image.open(ref_path).convert("RGB"))
    intact = np.asarray(Image.open(intact_path).convert("RGB"))
    out = np.empty_like(ref)
    for index in range(9):
        y0 = (index // 3) * 512
        x0 = (index % 3) * 512
        cell = ref[y0:y0 + 512, x0:x0 + 512]
        base = intact[y0:y0 + 512, x0:x0 + 512]
        painted = repaint_hit_flash(cell) if index == 8 else repaint_copper(cell, base, index, state)
        out[y0:y0 + 512, x0:x0 + 512] = painted

    # The reference is already at native pixel resolution.  Upscale only with
    # nearest-neighbour, preserving every painted pixel exactly 2x2.
    out = remove_magenta(out)
    image = Image.fromarray(out, mode="RGB").resize((3072, 3072), Image.Resampling.NEAREST)
    output = ROOT / f"skin_Night_state{state}.png"
    image.save(output, format="PNG", optimize=False)
    return output


def main() -> None:
    for state in range(3):
        print(build_sheet(state))


if __name__ == "__main__":
    main()
