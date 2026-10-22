"""Build the Night IonLancer sheets from the approved sheets.

This is intentionally a palette-aware repaint, not a geometric transform: each source
sheet is enlarged 2x with nearest-neighbour and its blue armour pixels are repainted
through a stepped night ramp.  The reference's non-armour pixels remain byte-identical.
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


def armour_mask(rgb: np.ndarray):
    hue, sat, value, r, g, b = hsv_arrays(rgb)
    # The canopy is a separate teal material.  This test deliberately keeps its
    # dark glass and bright glass out of the armour repaint.
    blue_glass = (
        (hue >= 165)
        & (hue <= 215)
        & (sat > 0.45)
        & (g > 20)
        & ((g / np.maximum(b, 1)) > 0.58)
        & ((g / np.maximum(b, 1)) < 1.08)
    )
    armour = (
        (hue >= 190)
        & (hue <= 250)
        & (sat > 0.38)
        & (value > 0.18)
        & (b > g * 1.08)
        & ~blue_glass
        # Very dark navy/black pixels in the damaged sheets are soot and voids,
        # not blue plate material, so leave them untouched.
        & ~((r < 20) & (g < 22) & (b < 45))
    )
    return armour, blue_glass, value, r, g, b


def repaint(source: Image.Image) -> Image.Image:
    rgb = np.asarray(source.convert("RGB"), dtype=np.uint8)
    out = rgb.copy()
    armour, _blue_glass, value, r, g, b = armour_mask(rgb)
    hue, saturation, _value, _r, _g, _b = hsv_arrays(rgb)

    # A hard, deliberately limited ramp.  The source value chooses the material
    # step, while the source's high-value edge pixels provide the sparse cyan
    # lighting accents.  This keeps panel texture, seams and damage placement
    # exactly where the approved drawing puts them.
    night_ramp = np.array(
        [
            (26, 31, 69),    # shadow #1A1F45
            (32, 38, 82),
            (42, 46, 107),   # base #2A2E6B
            (55, 65, 125),
            (72, 84, 148),
        ],
        dtype=np.uint8,
    )
    step = np.select(
        [value < 0.28, value < 0.42, value < 0.58, value < 0.74],
        [0, 1, 2, 3],
        default=4,
    )
    out[armour] = night_ramp[step[armour]]

    # Preserve the hand-placed character of the reference highlight pixels, but
    # change their material role to the requested Night cyan/pale-cyan accents.
    up = np.roll(armour, 1, axis=0)
    left = np.roll(armour, 1, axis=1)
    upper_left_edge = armour & (~up | ~left)
    cyan_rim = upper_left_edge & (value > 0.52) & (g / np.maximum(b, 1) > 0.30)
    out[cyan_rim] = (110, 242, 238)  # #6EF2EE

    pale_highlight = armour & ~cyan_rim & (value > 0.90) & (g / np.maximum(b, 1) > 0.58)
    out[pale_highlight] = (194, 235, 242)

    # The approved hit-flash pose is intentionally bone-white rather than blue,
    # but it still needs to carry the Night export's colourway delta.  Repaint its
    # non-ink bone/grey ramp to the warmer approved bone-white ramp; the silhouette,
    # ink, engines and damage marks remain untouched.
    hit = np.zeros(rgb.shape[:2], dtype=bool)
    hit[1024:1536, 1024:1536] = True
    neutral_bone = (
        hit
        & (r > 80)
        & (g > 70)
        & (b > 60)
        & ((np.maximum(np.maximum(r, g), b) - np.minimum(np.minimum(r, g), b)) < 80)
    )
    bone_step = np.empty((*value.shape, 3), dtype=np.uint8)
    bone_step[:] = (255, 251, 233)
    bone_step[value < 0.90] = (254, 249, 231)
    bone_step[value < 0.75] = (243, 233, 211)
    bone_step[value < 0.55] = (174, 163, 150)
    out[neutral_bone] = bone_step[neutral_bone]

    # The reference sheets contain a few purple edge-contamination pixels from
    # the old blue/red composite.  Night explicitly forbids magenta, so clean
    # those isolated pixels into the same local night/bone ramps.
    forbidden_magenta = (hue > 280) & (hue < 345) & (saturation > 0.30)
    out[forbidden_magenta & ~hit] = night_ramp[step[forbidden_magenta & ~hit]]
    out[forbidden_magenta & hit] = bone_step[forbidden_magenta & hit]

    # Avoid wraparound making a false edge at a sheet boundary.
    out[0, :, :] = rgb[0, :, :]
    out[-1, :, :] = rgb[-1, :, :]
    out[:, 0, :] = rgb[:, 0, :]
    out[:, -1, :] = rgb[:, -1, :]

    enlarged = Image.fromarray(out, mode="RGB").resize((3072, 3072), Image.Resampling.NEAREST)
    return enlarged


def main() -> None:
    for state in range(3):
        source_path = ROOT / f"skinref_state{state}.png"
        target_path = ROOT / f"skin_Night_state{state}.png"
        repaint(Image.open(source_path)).save(target_path, format="PNG", compress_level=9)
        print(target_path)


if __name__ == "__main__":
    main()
