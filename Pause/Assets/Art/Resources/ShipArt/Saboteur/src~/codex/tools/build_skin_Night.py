#!/usr/bin/env python3
"""Build the Night Saboteur sheets from the approved flipbook references.

The generated Night candidates are kept in this directory as painting references.
This finishing pass preserves the approved raster geometry and all non-armour FX,
then applies a discrete, hand-authored indigo/cyan material ramp and enlarges by
2x with nearest-neighbour.
"""

from pathlib import Path
import colorsys

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
PALETTE = np.array(
    [
        (26, 31, 69),    # #1A1F45 shadow
        (34, 40, 86),
        (42, 46, 107),   # #2A2E6B base
        (59, 76, 143),
        (82, 106, 180),
        (110, 242, 238), # #6EF2EE cyan rim
        (217, 255, 255),
    ],
    dtype=np.uint8,
)
HIT_PALETTE = np.array(
    [
        (182, 177, 167),
        (216, 209, 196),
        (241, 235, 219),
        (255, 250, 232),
    ],
    dtype=np.uint8,
)


def armour_mask(rgb: np.ndarray) -> np.ndarray:
    """Select violet/magenta armour pixels without touching red, orange, or teal FX."""
    flat = rgb.reshape(-1, 3).astype(np.float32) / 255.0
    hsv = np.array([colorsys.rgb_to_hsv(*p) for p in flat], dtype=np.float32)
    hue = hsv[:, 0] * 360.0
    sat = hsv[:, 1]
    val = hsv[:, 2]
    # The source armour is a tightly clustered violet ramp. The expanded hue
    # range also catches its dark purple facets and anti-aliased magenta edges.
    mask = (hue >= 258.0) & (hue <= 345.0) & (sat >= 0.24) & (val >= 0.07)
    # Keep only colours whose blue channel is a material component; this avoids
    # red/orange caps and sparks at the warm end of the range. Very dark
    # magenta shadows are intentionally included so no old pink remains.
    r, g, b = flat.T
    mask &= b >= g * 1.12
    return mask.reshape(rgb.shape[:2])


def paint_armour(rgb: np.ndarray) -> np.ndarray:
    out = rgb.copy()
    mask = armour_mask(rgb)
    src = rgb.astype(np.float32) / 255.0
    # Preserve the source's hard banding/texture by quantising its value into
    # the new material ramp rather than smoothing or hue-shifting it.
    val = np.max(src, axis=2)
    sat = np.max(src, axis=2) - np.min(src, axis=2)
    # The source is a very bright violet ramp. Compress it toward the dark
    # half of the Night ramp so the new metal reads as indigo-navy, reserving
    # cyan for only the source's hottest edge pixels.
    level = np.floor(np.clip((val - 0.10) * 5.0, 0, len(PALETTE) - 2))
    level[(val > 0.985) & (sat > 0.72)] = 5
    level[(val > 0.995) & (sat < 0.80)] = 6
    out[mask] = PALETTE[level[mask].astype(np.int32)]
    return out


def repaint_hit_flash(rgb: np.ndarray) -> np.ndarray:
    """Refresh the neutral bone-white ramp while keeping the hit silhouette intact."""
    out = rgb.copy()
    tile = out[1024:1536, 1024:1536]
    r, g, b = tile.astype(np.int16).transpose(2, 0, 1)
    neutral = (np.maximum.reduce([r, g, b]) > 90)
    neutral &= (np.max(np.stack([r, g, b]), axis=0) - np.min(np.stack([r, g, b]), axis=0) < 70)
    value = np.max(np.stack([r, g, b]), axis=0) / 255.0
    level = np.clip((value - 0.34) * 6.0, 0, len(HIT_PALETTE) - 1)
    tile[neutral] = HIT_PALETTE[np.rint(level[neutral]).astype(np.int32)]
    out[1024:1536, 1024:1536] = tile
    return out


def build(state: int) -> None:
    ref_path = ROOT / f"skinref_state{state}.png"
    out_path = ROOT / f"skin_Night_state{state}.png"
    ref = np.asarray(Image.open(ref_path).convert("RGB"))
    painted = repaint_hit_flash(paint_armour(ref))
    final = Image.fromarray(painted, "RGB").resize((3072, 3072), Image.Resampling.NEAREST)
    final.save(out_path, format="PNG", optimize=False)


if __name__ == "__main__":
    for state in range(3):
        build(state)
