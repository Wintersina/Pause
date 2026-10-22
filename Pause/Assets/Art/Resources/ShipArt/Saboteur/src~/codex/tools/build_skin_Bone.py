#!/usr/bin/env python3
"""Build the Bone Saboteur sheets from the approved 3x3 flipbook references.

The three generated Bone candidates in this directory were used as repaint
references.  This finishing pass keeps the approved raster geometry and every
non-armour pixel, then paints the armour with a discrete warm ceramic ramp and
enlarges the result 2x with nearest-neighbour.  That makes the colourway a
faithful cell-for-cell repaint instead of a geometry-changing AI redraw.
"""

from pathlib import Path
import colorsys

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)

# Bone ceramic ramp, dark to light.  The middle two colours are the requested
# shadow and base; the upper steps are reserved for hard ceramic highlights.
BONE_RAMP = np.array(
    [
        (72, 66, 57),       # deep warm ceramic shadow
        (126, 117, 101),    # recessed plane
        (185, 174, 152),    # #B9AE98 shadow
        (244, 234, 212),    # #F4EAD4 base
        (252, 246, 230),    # warm light
        (255, 250, 232),    # near-white highlight
        (255, 255, 248),    # white-hot ceramic glint
    ],
    dtype=np.uint8,
)

# The approved hit-flash cell is already neutral ceramic.  Retone its ceramic
# planes with a fresh Bone ramp so the finished colourway still has a real
# repaint in every cell while preserving the hit-flash's bone-white read.
HIT_RAMP = np.array(
    [
        (112, 104, 91),
        (170, 160, 142),
        (210, 201, 183),
        (244, 234, 212),
        (255, 250, 232),
        (255, 255, 248),
    ],
    dtype=np.uint8,
)


def green_background(rgb: np.ndarray) -> np.ndarray:
    """Return the reference's flat-screen pixels, including tiny green fringe."""
    r, g, b = rgb.astype(np.int16).transpose(2, 0, 1)
    return (g > 180) & (g > r * 1.6) & (g > b * 1.6)


def armour_mask(rgb: np.ndarray) -> np.ndarray:
    """Select only the original violet armour ramp, not red/teal/amber FX."""
    flat = rgb.reshape(-1, 3).astype(np.float32) / 255.0
    hsv = np.array([colorsys.rgb_to_hsv(*p) for p in flat], dtype=np.float32)
    hue = hsv[:, 0] * 360.0
    sat = hsv[:, 1]
    value = hsv[:, 2]
    r, g, b = flat.T
    mask = (hue >= 258.0) & (hue <= 345.0)
    # Include pale lavender anti-aliased armour highlights as well as the
    # saturated violet ramp; otherwise the old hue can leave pink pixels on
    # otherwise bone-white plate edges.
    mask &= sat >= 0.08
    mask &= value >= 0.07
    mask &= b >= g * 1.12
    return mask.reshape(rgb.shape[:2])


def ramp_indices(value: np.ndarray, ramp_size: int) -> np.ndarray:
    """Quantise source luminance into hard, painterly ceramic bands."""
    # These breaks keep deep facets dark, put most plating on the requested
    # base, and reserve near-white for the source's existing rim highlights.
    breaks = np.array([0.10, 0.18, 0.30, 0.55, 0.78, 0.93], dtype=np.float32)
    return np.clip(np.digitize(value, breaks), 0, ramp_size - 1)


def paint_armour(rgb: np.ndarray) -> np.ndarray:
    out = rgb.copy()
    mask = armour_mask(rgb)
    value = np.max(rgb.astype(np.float32), axis=2) / 255.0
    indices = ramp_indices(value, len(BONE_RAMP))
    out[mask] = BONE_RAMP[indices[mask]]
    return out


def repaint_hit_flash(rgb: np.ndarray) -> np.ndarray:
    """Retone only neutral ceramic planes in the final hit-flash cell."""
    out = rgb.copy()
    tile = out[1024:1536, 1024:1536].copy()
    r, g, b = tile.astype(np.int16).transpose(2, 0, 1)
    value = np.max(np.stack([r, g, b]), axis=0) / 255.0
    chroma = np.max(np.stack([r, g, b]), axis=0) - np.min(np.stack([r, g, b]), axis=0)
    neutral = (value > 0.22) & (chroma < 42)
    # Keep the exact approved green, ink, teal core, red lights, amber nozzle,
    # and any saturated hit FX untouched.
    neutral &= ~green_background(tile)
    indices = np.clip(np.digitize(value, np.array([0.38, 0.58, 0.76, 0.91, 0.985])), 0, len(HIT_RAMP) - 1)
    tile[neutral] = HIT_RAMP[indices[neutral]]
    out[1024:1536, 1024:1536] = tile
    return out


def neutralize_forbidden_magenta(rgb: np.ndarray) -> np.ndarray:
    """Remove pale magenta fringe pixels left by source rasterisation."""
    out = rgb.copy()
    flat = out.reshape(-1, 3).astype(np.float32) / 255.0
    hsv = np.array([colorsys.rgb_to_hsv(*p) for p in flat], dtype=np.float32)
    hue = hsv[:, 0] * 360.0
    sat = hsv[:, 1]
    value = hsv[:, 2]
    # Use a small tolerance around the hue boundary because 8-bit warm-grey
    # fringe pixels can quantise to a nominal hue of exactly 280 degrees.
    forbidden = (hue >= 275.0) & (hue <= 345.0) & (sat >= 0.075)
    forbidden &= value >= 0.07
    replacement = BONE_RAMP[ramp_indices(value, len(BONE_RAMP))]
    out.reshape(-1, 3)[forbidden] = replacement[forbidden]
    return out


def build(state: int) -> None:
    ref_path = ROOT / f"skinref_state{state}.png"
    out_path = ROOT / f"skin_Bone_state{state}.png"
    ref = np.asarray(Image.open(ref_path).convert("RGB"))
    painted = neutralize_forbidden_magenta(repaint_hit_flash(paint_armour(ref)))
    # Make the background genuinely flat pure green; this does not alter the
    # non-green silhouette or any ship pixel.
    painted[green_background(painted)] = GREEN
    final = Image.fromarray(painted, "RGB").resize((3072, 3072), Image.Resampling.NEAREST)
    final.save(out_path, format="PNG", optimize=False)


if __name__ == "__main__":
    for state in range(3):
        build(state)
