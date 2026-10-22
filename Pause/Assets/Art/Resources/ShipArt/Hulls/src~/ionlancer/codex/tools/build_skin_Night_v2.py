"""Build the requested Night damaged/wreck sheets as new, non-destructive files.

The approved 1536px sheets are the geometry source of truth.  This builder changes
the cobalt armour ramp at native resolution, preserves every non-armour pixel, then
exports a 2x nearest-neighbour sheet for Unity.
"""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)


def hsv_arrays(rgb: np.ndarray):
    r, g, b = (rgb[..., i].astype(np.float32) for i in range(3))
    mx = rgb.max(axis=2).astype(np.float32)
    mn = rgb.min(axis=2).astype(np.float32)
    delta = mx - mn
    hue = np.zeros_like(mx)
    nz = delta > 0
    rr, gg, bb, mm, dd = r[nz], g[nz], b[nz], mx[nz], delta[nz]
    hue[nz] = np.where(
        mm == rr,
        (gg - bb) / dd,
        np.where(mm == gg, 2 + (bb - rr) / dd, 4 + (rr - gg) / dd),
    ) * 60.0
    hue %= 360.0
    saturation = np.divide(delta, mx, out=np.zeros_like(delta), where=mx > 0)
    value = mx / 255.0
    return hue, saturation, value, r, g, b


def material_masks(rgb: np.ndarray):
    hue, sat, value, r, g, b = hsv_arrays(rgb)

    # The canopy is teal glass, not armour.  Keep its complete pixel family out of
    # the repaint, including dark glass and cyan-white reflections.
    blue_glass = (
        (hue >= 160)
        & (hue <= 220)
        & (sat > 0.34)
        & (g > 16)
        & ((g / np.maximum(b, 1)) > 0.54)
        & ((g / np.maximum(b, 1)) < 1.16)
    )

    # Include the darker cobalt plates in the damaged/wreck frames.  Gunmetal is
    # much less saturated and the orange damage/embers sit outside this hue band.
    armour = (
        (hue >= 175)
        & (hue <= 275)
        & (sat > 0.20)
        & (value > 0.10)
        & (b > r * 1.08)
        & (b >= g * 0.98)
        & ~blue_glass
        & ~((r < 20) & (g < 22) & (b < 45))
    )

    # Wreck frames expose dark, low-saturation cobalt substrate around the
    # missing plates.  Grow only into neutral pixels directly touching cobalt;
    # warm damage, soot-black voids, engines, and teal glass remain protected.
    near_armour = armour.copy()
    for _ in range(6):
        expanded = near_armour.copy()
        expanded[1:] |= near_armour[:-1]
        expanded[:-1] |= near_armour[1:]
        expanded[:, 1:] |= near_armour[:, :-1]
        expanded[:, :-1] |= near_armour[:, 1:]
        near_armour = expanded
    neutral_substrate = (
        (sat < 0.40)
        & (value > 0.01)
        & ~blue_glass
        & ~((sat > 0.10) & ((hue < 55) | (hue > 330)))
    )
    armour |= neutral_substrate & near_armour
    # Low-value warm pixels adjacent to exposed cobalt are the dark painted
    # substrate under the wreck's damage.  Leave bright orange tears and embers
    # untouched; only this soot-dark base participates in the new metal ramp.
    warm_substrate = (
        (sat > 0.10)
        & ((hue < 55) | (hue > 330))
        & (value < 0.45)
        & ~blue_glass
    )
    armour |= warm_substrate & near_armour
    return armour, blue_glass, hue, sat, value, r, g, b


def repaint(source: Image.Image) -> Image.Image:
    rgb = np.asarray(source.convert("RGB"), dtype=np.uint8)
    out = rgb.copy()
    armour, blue_glass, hue, sat, value, r, g, b = material_masks(rgb)

    # Deep indigo metal ramp.  The exact requested base and shadow are present;
    # intermediate steps retain the reference's hard painted value structure.
    ramp = np.array(
        [
            (26, 31, 69),    # #1A1F45 shadow
            (34, 39, 82),
            (42, 46, 107),   # #2A2E6B base
            (59, 68, 126),
            (83, 96, 156),
        ],
        dtype=np.uint8,
    )
    step = np.select(
        [value < 0.24, value < 0.38, value < 0.56, value < 0.76],
        [0, 1, 2, 3],
        default=4,
    )
    out[armour] = ramp[step[armour]]

    # Preserve the hand-placed edge pattern while changing its colour role to the
    # Night neon rim.  Only edges already belonging to armour can be affected.
    up = np.zeros_like(armour)
    left = np.zeros_like(armour)
    up[1:] = armour[:-1]
    left[:, 1:] = armour[:, :-1]
    upper_left_edge = armour & ~(up & left)
    cyan_rim = upper_left_edge & (value > 0.43)
    out[cyan_rim] = (110, 242, 238)  # #6EF2EE

    pale = armour & ~cyan_rim & (value > 0.995) & (sat > 0.50) & ((b - r) > 60)
    out[pale] = (194, 235, 242)

    # The ninth pose is a bone-white hit flash in both approved sheets.  Keep it
    # bone-white while allowing its shaded material ramp to remain visibly repainted.
    hit = np.zeros(rgb.shape[:2], dtype=bool)
    hit[1024:1536, 1024:1536] = True
    neutral_bone = (
        hit
        & (r > 80)
        & (g > 70)
        & (b > 60)
        & ((np.maximum(np.maximum(r, g), b) - np.minimum(np.minimum(r, g), b)) < 80)
    )
    bone = np.empty((*value.shape, 3), dtype=np.uint8)
    bone[:] = (255, 251, 233)
    bone[value < 0.90] = (254, 249, 231)
    bone[value < 0.75] = (243, 233, 211)
    bone[value < 0.55] = (174, 163, 150)
    out[neutral_bone] = bone[neutral_bone]

    # Remove any magenta contamination without touching the teal canopy or red
    # player lights.  These source pixels are edge contamination, not a material.
    magenta = (hue > 280) & (hue < 345) & (sat > 0.30)
    out[magenta & ~hit] = ramp[step[magenta & ~hit]]
    out[magenta & hit] = bone[magenta & hit]

    # Background must remain exactly flat green, even if a source edge pixel was
    # classified by the broad blue mask.
    out[np.all(rgb == GREEN, axis=2)] = GREEN

    enlarged = Image.fromarray(out, mode="RGB").resize((3072, 3072), Image.Resampling.NEAREST)
    return enlarged


def main() -> None:
    for state in (1, 2):
        source_path = ROOT / f"skinref_state{state}.png"
        target_path = ROOT / f"skin_Night_state{state}_v2.png"
        repaint(Image.open(source_path)).save(target_path, format="PNG", compress_level=9)
        print(target_path)


if __name__ == "__main__":
    main()
