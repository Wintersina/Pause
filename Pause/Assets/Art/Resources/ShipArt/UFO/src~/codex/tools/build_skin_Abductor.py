"""Build the Abductor UFO colourway from the approved flipbook references.

The image-generation candidates establish the teal material ramp.  This final paint pass keeps
the approved raster as the registration source of truth: no pixels are moved, resampled with a
filter, or added to the silhouettes.  Only qualifying armour pixels on the right side of each
cell are repainted, leaving the left violet armour, hub, canopy, lights, and damage untouched.
"""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)

# Abductor's hard, stepped teal ceramic ramp.  The three named colours are the requested
# material anchors; the two intermediate steps keep the source's painterly value structure.
TEAL_DEEP = np.array([10, 62, 72], dtype=np.uint8)
TEAL_SHADOW = np.array([15, 94, 106], dtype=np.uint8)       # #0F5E6A
TEAL_BASE = np.array([31, 181, 185], dtype=np.uint8)        # #1FB5B9
TEAL_LIGHT = np.array([73, 214, 211], dtype=np.uint8)
TEAL_HIGHLIGHT = np.array([110, 242, 238], dtype=np.uint8)  # #6EF2EE

VIOLET_SHADOW = np.array([58, 36, 120], dtype=np.uint8)       # #3A2478
VIOLET_BASE = np.array([122, 82, 220], dtype=np.uint8)        # #7A52DC
VIOLET_HIGHLIGHT = np.array([187, 164, 255], dtype=np.uint8)  # #BBA4FF

GUNMETAL_DARK = np.array([47, 52, 59], dtype=np.uint8)
GUNMETAL_MID = np.array([75, 80, 89], dtype=np.uint8)
GUNMETAL_LIGHT = np.array([122, 124, 130], dtype=np.uint8)


def hsv(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Return hue in degrees, saturation, and value for an RGB uint8 image."""

    c = rgb.astype(np.float32) / 255.0
    mx = c.max(axis=-1)
    mn = c.min(axis=-1)
    delta = mx - mn
    r, g, b = c[..., 0], c[..., 1], c[..., 2]
    hue = np.zeros_like(mx)
    valid = delta > 1e-6
    sel = valid & (mx == r)
    hue[sel] = ((g[sel] - b[sel]) / delta[sel]) % 6.0
    sel = valid & (mx == g)
    hue[sel] = (b[sel] - r[sel]) / delta[sel] + 2.0
    sel = valid & (mx == b)
    hue[sel] = (r[sel] - g[sel]) / delta[sel] + 4.0
    return hue * 60.0, np.divide(delta, mx, out=np.zeros_like(delta), where=mx > 0), mx


def armour_mask(rgb: np.ndarray) -> np.ndarray:
    """Select visible violet armour while excluding the blue hub/canopy and deep ink.

    The radial guard protects the central gunmetal assembly.  The colour guards exclude cyan
    glass and orange/red damage, while the value floor leaves the reference's ink and light-off
    sockets byte-identical.
    """

    h, s, v = hsv(rgb)
    yy, xx = np.indices(rgb.shape[:2])
    radial = np.sqrt((xx - 256.0) ** 2 + (yy - 256.0) ** 2)
    return (
        ~np.all(rgb == GREEN, axis=-1)
        & (radial >= 104.0)
        & (h >= 240.0)
        & (h <= 275.0)
        & (s >= 0.38)
        & (v >= 0.30)
        & ((rgb[..., 2].astype(np.int16) - rgb[..., 0].astype(np.int16)) >= 28)
        & ((rgb[..., 2].astype(np.int16) - rgb[..., 1].astype(np.int16)) >= 8)
    )


def teal_ramp(value: np.ndarray) -> np.ndarray:
    """Quantise source value into Abductor's hard material ramp."""

    out = np.empty((*value.shape, 3), dtype=np.uint8)
    out[value < 0.42] = TEAL_DEEP
    out[(value >= 0.42) & (value < 0.62)] = TEAL_SHADOW
    out[(value >= 0.62) & (value < 0.82)] = TEAL_BASE
    out[(value >= 0.82) & (value < 0.95)] = TEAL_LIGHT
    out[value >= 0.95] = TEAL_HIGHLIGHT
    return out


def repaint_cell(ref: np.ndarray, row: int, col: int) -> np.ndarray:
    # The approved hit-flash is bone-white and must remain completely unchanged.
    if row == 2 and col == 2:
        return ref.copy()

    out = ref.copy()
    mask = armour_mask(ref)
    # A split is defined in native 512px cell coordinates: x=256 is the centre line.
    mask &= np.indices(mask.shape)[1] >= 256
    if np.any(mask):
        _, _, value = hsv(ref)
        ramp = teal_ramp(value)
        out[mask] = ramp[mask]

    # The approved source contains a few high-saturation pink/magenta damage pixels.  The
    # requested palette forbids those hues, so neutralize only those pixels into the existing
    # violet material ramp.  Their locations and silhouettes remain unchanged.
    h, s, value = hsv(out)
    magenta = (
        ~np.all(out == GREEN, axis=-1)
        & (h >= 300.0)
        & (h <= 345.0)
        & (s > 0.50)
        & (value > 0.08)
    )
    violet_ramp = np.empty_like(out)
    violet_ramp[value < 0.42] = VIOLET_SHADOW
    violet_ramp[(value >= 0.42) & (value < 0.82)] = VIOLET_BASE
    violet_ramp[value >= 0.82] = VIOLET_HIGHLIGHT
    out[magenta] = violet_ramp[magenta]

    # Remove green-tinted non-background edge pixels present in a few wreck/damage source
    # pixels.  Pure #00FF00 remains the sheet background; only ship pixels are neutralized.
    h, s, value = hsv(out)
    green_cast = (
        ~np.all(out == GREEN, axis=-1)
        & (h >= 75.0)
        & (h <= 165.0)
        & (s > 0.03)
        & (value > 0.12)
    )
    gunmetal_ramp = np.empty_like(out)
    gunmetal_ramp[value < 0.42] = GUNMETAL_DARK
    gunmetal_ramp[(value >= 0.42) & (value < 0.72)] = GUNMETAL_MID
    gunmetal_ramp[value >= 0.72] = GUNMETAL_LIGHT
    out[green_cast] = gunmetal_ramp[green_cast]
    return out


def build(state: int) -> Path:
    ref_path = ROOT / f"skinref_state{state}.png"
    ref = np.array(Image.open(ref_path).convert("RGB"), dtype=np.uint8)
    canvas = np.empty((3072, 3072, 3), dtype=np.uint8)
    canvas[:, :] = GREEN

    for row in range(3):
        for col in range(3):
            y0, y1 = row * 512, (row + 1) * 512
            x0, x1 = col * 512, (col + 1) * 512
            painted = repaint_cell(ref[y0:y1, x0:x1], row, col)
            scaled = Image.fromarray(painted, "RGB").resize((1024, 1024), Image.Resampling.NEAREST)
            canvas[row * 1024:(row + 1) * 1024, col * 1024:(col + 1) * 1024] = np.asarray(scaled)

    out_path = ROOT / f"skin_Abductor_state{state}.png"
    Image.fromarray(canvas, "RGB").save(out_path, optimize=False)
    return out_path


if __name__ == "__main__":
    for state_id in range(3):
        print(build(state_id))
