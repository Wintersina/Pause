#!/usr/bin/env python3
"""Build the Sodium Saboteur sheets from the approved flipbook.

The image-generation candidates in the parent codex directory were used as
the visual repaint guide.  This finishing pass keeps the approved raster
geometry and all non-armour pixels byte-for-byte, then paints the armour with
an explicit stepped sodium metal ramp before a 2x nearest-neighbour export.
"""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]

# Warm sodium-lamp metal ramp, dark to hot.  These are deliberately discrete
# bands, not a hue rotation or a continuous gradient.
SODIUM_RAMP = np.array(
    [
        (169, 72, 26),       # shadow #A9481A
        (198, 101, 29),
        (242, 134, 43),      # base #F2862B
        (250, 151, 42),
        (255, 180, 60),      # highlight #FFB43C
        (255, 211, 111),
        (255, 242, 184),     # pale yellow-white hot edge
    ],
    dtype=np.uint8,
)
HIT_PALETTE = np.array(
    [
        (184, 177, 161),
        (218, 209, 190),
        (241, 233, 213),
        (255, 249, 229),
    ],
    dtype=np.uint8,
)


def hsv(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Small vectorised RGB -> HSV conversion with hue in degrees."""
    f = rgb.astype(np.float32) / 255.0
    r, g, b = f[..., 0], f[..., 1], f[..., 2]
    mx = np.max(f, axis=2)
    mn = np.min(f, axis=2)
    delta = mx - mn
    h = np.zeros_like(mx)
    nz = delta > 1e-6
    with np.errstate(divide="ignore", invalid="ignore"):
        rmax = nz & (mx == r)
        gmax = nz & (mx == g)
        bmax = nz & (mx == b)
        h[rmax] = ((g - b) / delta)[rmax] % 6.0
        h[gmax] = ((b - r) / delta + 2.0)[gmax]
        h[bmax] = ((r - g) / delta + 4.0)[bmax]
    h *= 60.0
    s = np.divide(delta, mx, out=np.zeros_like(mx), where=mx > 1e-6)
    return h, s, mx


def armour_mask(rgb: np.ndarray) -> np.ndarray:
    """Select the source violet armour, not cyan glass, red caps, or ink."""
    h, s, value = hsv(rgb)
    r, g, b = rgb.astype(np.float32).transpose(2, 0, 1)
    # The source armour is violet with blue materially above green.  The
    # value floor leaves the black selective ink untouched; the blue test
    # keeps the teal core and red cap pixels out of the repaint.
    mask = (h >= 250.0) & (h <= 350.0) & (s >= 0.08) & (value >= 0.12)
    mask &= b >= g * 1.02
    mask &= b >= r * 0.96
    # Cell 8 is the deliberately neutral hit-flash ship, not sodium armour.
    mask[1024:1536, 1024:1536] = False
    return mask


def paint_armour(rgb: np.ndarray) -> np.ndarray:
    out = rgb.copy()
    mask = armour_mask(rgb)
    _, _, value = hsv(rgb)

    # Map source luminance bands into the authored sodium ramp.  A small
    # source-value-dependent banding bias retains the original facets,
    # scratches and panel texture while changing the material response.
    level = np.select(
        [
            value < 0.12,
            value < 0.22,
            value < 0.38,
            value < 0.56,
            value < 0.76,
            value < 0.92,
            value < 0.985,
        ],
        [0, 1, 2, 2, 3, 4, 5],
        default=6,
    ).astype(np.int32)
    out[mask] = SODIUM_RAMP[level[mask]]
    return out


def repaint_hit_flash(rgb: np.ndarray) -> np.ndarray:
    """Refresh the neutral bone-white hit cell without changing its role."""
    out = rgb.copy()
    tile = out[1024:1536, 1024:1536]
    channels = tile.astype(np.int16)
    maximum = np.max(channels, axis=2)
    minimum = np.min(channels, axis=2)
    neutral = (maximum > 90) & ((maximum - minimum) < 70)
    level = np.clip(((maximum.astype(np.float32) - 80.0) / 48.0), 0, 3).astype(np.int32)
    tile[neutral] = HIT_PALETTE[level[neutral]]
    out[1024:1536, 1024:1536] = tile
    return out


def build(state: int) -> None:
    ref = np.asarray(Image.open(ROOT / f"skinref_state{state}.png").convert("RGB"))
    painted = repaint_hit_flash(paint_armour(ref))
    final = Image.fromarray(painted, "RGB").resize((3072, 3072), Image.Resampling.NEAREST)
    final.save(ROOT / f"skin_Sodium_state{state}.png", format="PNG", optimize=False)


if __name__ == "__main__":
    for state in range(3):
        build(state)
