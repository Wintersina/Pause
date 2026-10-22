"""Build the Bone UFO colourway from the approved flipbook sheets.

The approved sheets remain the registration and drawing source of truth.  This repaint uses a
stepped ceramic ramp, local edge exposure, and the generated Bone candidates as the visual
direction; it does not move or redraw any silhouette pixels.  The finished 2x export is nearest
neighbour so the 512px reference cells become 1024px cells without filtering.
"""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)


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
    """Select violet ceramic plating, excluding teal/cyan canopy and gunmetal hub."""

    h, s, v = hsv(rgb)
    green = np.all(rgb == GREEN, axis=-1)
    # The approved violet ramp is distinctly blue-violet.  The lower value guard leaves the
    # ink edge, deep sockets, and the dark light-off star centres unchanged.
    return (
        ~green
        & (h >= 235.0)
        & (h <= 285.0)
        & (s > 0.28)
        & (v > 0.12)
        & (rgb[..., 2] > rgb[..., 0].astype(np.int16) + 18)
        & (rgb[..., 2] > rgb[..., 1].astype(np.int16) + 8)
        & (rgb[..., 0] >= rgb[..., 1].astype(np.float32) * 0.78)
    )


def repaint_armour(rgb: np.ndarray, row: int, col: int) -> np.ndarray:
    out = rgb.copy()

    # The approved hit-flash is already the bone-white flash state.
    if row == 2 and col == 2:
        return out

    mask = armour_mask(rgb)
    if not np.any(mask):
        return out

    _, _, value = hsv(rgb)

    # Hard ceramic ramp: warm-grey seams/shadows, requested shadow/base, and sparse white-hot
    # highlights.  These are painted material tones, not a hue rotation of the source pixels.
    ramp = np.empty_like(rgb)
    ramp[value < 0.22] = (92, 86, 75)       # deep warm-grey ceramic shadow
    ramp[(value >= 0.22) & (value < 0.34)] = (137, 128, 112)
    ramp[(value >= 0.34) & (value < 0.50)] = (185, 174, 152)  # #B9AE98
    ramp[(value >= 0.50) & (value < 0.68)] = (213, 202, 181)
    ramp[(value >= 0.68) & (value < 0.86)] = (244, 234, 212)  # #F4EAD4
    ramp[(value >= 0.86) & (value < 0.95)] = (250, 245, 232)
    ramp[value >= 0.95] = (255, 255, 255)

    # Keep the top-left edge treatment crisp and ceramic without introducing any new outline or
    # geometry.  Exposed bright source pixels become the sparse pure-white edge glints seen in
    # the generated Bone candidates.
    padded = np.pad(mask, 1, constant_values=False)
    upper_left_exposed = (
        (~padded[:-2, :-2])
        | (~padded[:-2, 1:-1])
        | (~padded[1:-1, :-2])
    )
    edge_highlight = mask & upper_left_exposed & (value >= 0.80)
    ramp[edge_highlight] = (255, 255, 255)

    out[mask] = ramp[mask]
    return out


def remove_forbidden_casts(rgb: np.ndarray) -> np.ndarray:
    """Remove tiny source-screen contamination without moving any damage or edge pixels."""

    out = rgb.copy()
    h, s, v = hsv(out)
    green = np.all(out == GREEN, axis=-1)

    # The references contain a few near-black violet pixels and mint anti-alias pixels in the
    # wreck/damage art.  They are not part of the approved Bone palette.  Keep the same pixels
    # non-green and in place, but neutralize them to the ceramic's warm-grey ramp.
    magenta = ~green & (h >= 300.0) & (h <= 345.0) & (s > 0.20) & (v > 0.06)
    # Stop below the checker’s teal/canopy hue band so canopy pixels remain byte-identical.
    green_cast = ~green & (h >= 75.0) & (h <= 155.0) & (s > 0.08) & (v > 0.12)
    bad = magenta | green_cast
    neutral = np.empty_like(out)
    neutral[v < 0.22] = (92, 86, 75)
    neutral[(v >= 0.22) & (v < 0.50)] = (185, 174, 152)
    neutral[(v >= 0.50) & (v < 0.86)] = (244, 234, 212)
    neutral[v >= 0.86] = (255, 255, 255)
    out[bad] = neutral[bad]
    return out


def build(state: int) -> Path:
    ref_path = ROOT / f"skinref_state{state}.png"
    ref = np.array(Image.open(ref_path).convert("RGB"), dtype=np.uint8)
    canvas = np.zeros((3072, 3072, 3), dtype=np.uint8)

    for row in range(3):
        for col in range(3):
            y0, y1 = row * 512, (row + 1) * 512
            x0, x1 = col * 512, (col + 1) * 512
            painted = remove_forbidden_casts(repaint_armour(ref[y0:y1, x0:x1], row, col))
            scaled = Image.fromarray(painted, "RGB").resize((1024, 1024), Image.Resampling.NEAREST)
            canvas[row * 1024:(row + 1) * 1024, col * 1024:(col + 1) * 1024] = np.array(scaled)

    out_path = ROOT / f"skin_Bone_state{state}.png"
    Image.fromarray(canvas, "RGB").save(out_path, optimize=False)
    return out_path


if __name__ == "__main__":
    for state_id in range(3):
        print(build(state_id))
