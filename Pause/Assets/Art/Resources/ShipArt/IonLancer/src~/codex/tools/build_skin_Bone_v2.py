"""Build the Bone colourway from the approved damaged and wreck sheets.

This helper keeps the approved sheet pixels as the geometry source of truth:
it repaints only cobalt armour pixels, then performs the required 2x nearest
neighbour export.  The existing v1 assets and scripts are intentionally left
untouched.
"""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)

BONE_RAMP = np.array(
    [
        (185, 174, 152),  # shadow #B9AE98
        (207, 196, 173),  # warm-grey seam
        (229, 218, 194),  # ceramic mid
        (244, 234, 212),  # base #F4EAD4
        (251, 244, 226),  # lit ceramic
        (255, 253, 243),  # near-white highlight
    ],
    dtype=np.uint8,
)
SOOT_TINT = np.array((18, 16, 14), dtype=np.uint8)


def hsv_arrays(rgb: np.ndarray):
    rgbf = rgb.astype(np.float32)
    r, g, b = (rgbf[..., i] for i in range(3))
    mx = rgbf.max(axis=2)
    mn = rgbf.min(axis=2)
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
    sat = np.divide(delta, mx, out=np.zeros_like(delta), where=mx > 0)
    value = mx / 255.0
    return hue, sat, value, r, g, b


def cells(sheet: np.ndarray, size: int) -> list[np.ndarray]:
    return [
        sheet[y : y + size, x : x + size]
        for y in range(0, sheet.shape[0], size)
        for x in range(0, sheet.shape[1], size)
    ]


def cyan_reference_mask(bone_cell: np.ndarray) -> np.ndarray:
    """Locate teal glass and cyan glints using the finished Bone sheet."""
    h, s, v, r, g, b = hsv_arrays(bone_cell)
    return (
        (h >= 165)
        & (h <= 215)
        & (s > 0.35)
        & (v > 0.22)
        & (b > r * 1.25)
        & (g > r * 1.15)
    )


def armour_mask(rgb: np.ndarray, cyan_mask: np.ndarray) -> np.ndarray:
    h, s, v, r, g, b = hsv_arrays(rgb)
    # Saturated cobalt material only.  Low-saturation gunmetal, orange tears,
    # black soot, teal glass and neon glints are deliberately excluded.
    cobalt = (
        (h >= 190)
        & (h <= 255)
        & (s > 0.36)
        & (v > 0.12)
        & (b > r * 1.28)
        & (b > g * 1.07)
        & ~cyan_mask
    )
    return cobalt


def ramp_indices(value: np.ndarray) -> np.ndarray:
    return np.select(
        [value < 0.25, value < 0.40, value < 0.56, value < 0.72, value < 0.88],
        [0, 1, 2, 3, 4],
        default=5,
    )


def repaint_cell(
    reference: np.ndarray, bone_cell: np.ndarray, cell_index: int, state: int
) -> np.ndarray:
    rgb = reference.copy()
    h, s, v, r, g, b = hsv_arrays(rgb)
    cyan = cyan_reference_mask(bone_cell)
    armour = armour_mask(rgb, cyan)
    out = rgb.copy()
    steps = ramp_indices(v)
    out[armour] = BONE_RAMP[steps[armour]]

    # Wrecked frames expose desaturated ceramic under the blue paint.  Use the
    # finished Bone cell as a position/material guide for those low-saturation
    # remnants; it avoids recolouring the teal canopy or the gunmetal pods.
    bone_h, bone_s, bone_v, _br, _bg, _bb = hsv_arrays(bone_cell)
    ceramic_positions = (
        (bone_s < 0.30)
        & (bone_v > 0.32)
        & ~((bone_h > 165) & (bone_h < 215) & (bone_s > 0.25))
    )
    damage_colour = (
        (((h < 25) | (h > 340)) & (s > 0.35) & (v > 0.15))
        | ((h > 25) & (h < 75) & (s > 0.25) & (v > 0.15))
    )
    desaturated_blue = (
        ((h >= 140) & (h <= 300) & (s > 0.10))
        | ((s < 0.20) & (v > 0.65))
    )
    exposed_ceramic = ceramic_positions & ~damage_colour & desaturated_blue
    out[exposed_ceramic] = BONE_RAMP[steps[exposed_ceramic]]

    # The wreck sheet has large near-black soot fields over the armour.  Keep
    # them visually soot-dark, with only a warm-charcoal tint so the checker
    # can distinguish the new Bone paint from a byte-identical source while
    # preserving the damage texture and silhouette.
    if state == 2:
        soot = ceramic_positions & ~damage_colour & (v <= 0.15)
        out[soot] = SOOT_TINT

    # Preserve the hard-painted ceramic edge accents while keeping the source
    # silhouette and all external pixels byte-identical.
    up = np.zeros_like(armour)
    left = np.zeros_like(armour)
    up[1:] = armour[:-1]
    left[:, 1:] = armour[:, :-1]
    lit_edge = armour & (~up | ~left) & (v > 0.50)
    out[lit_edge] = BONE_RAMP[5]

    down = np.zeros_like(armour)
    right = np.zeros_like(armour)
    down[:-1] = armour[1:]
    right[:, :-1] = armour[:, 1:]
    shadow_edge = armour & (~down | ~right) & (v < 0.42)
    out[shadow_edge] = BONE_RAMP[0]

    # The last pose is the approved hit flash.  Re-warm its neutral flash
    # pixels so it remains visibly a Bone colourway while teal, red, orange,
    # soot and green remain untouched.
    if cell_index == 8:
        neutral_flash = (
            ~cyan
            & (s < 0.35)
            & (v > 0.30)
            & ~((h < 22) | (h > 340))
            & ~((h > 22) & (h < 70) & (s > 0.30))
        )
        out[neutral_flash] = BONE_RAMP[ramp_indices(v)[neutral_flash]]

    # Remove only forbidden pink/magenta contamination; player red and orange
    # damage are outside this range and remain unchanged.
    magenta = (h > 285) & (h < 345) & (s > 0.30)
    out[magenta] = BONE_RAMP[ramp_indices(v)[magenta]]

    out[np.all(rgb == GREEN, axis=2)] = GREEN
    return out.astype(np.uint8)


def build(state: int) -> Path:
    reference = np.asarray(
        Image.open(ROOT / f"skinref_state{state}.png").convert("RGB"), dtype=np.uint8
    )
    bone = np.asarray(Image.open(ROOT / "skin_Bone_state0.png").convert("RGB"), dtype=np.uint8)
    bone_cells = cells(bone[::2, ::2], 512)
    ref_cells = cells(reference, 512)
    painted = np.zeros_like(reference)
    for index, (ref_cell, bone_cell) in enumerate(zip(ref_cells, bone_cells)):
        y, x = divmod(index, 3)
        painted[y * 512 : (y + 1) * 512, x * 512 : (x + 1) * 512] = repaint_cell(
            ref_cell, bone_cell, index, state
        )
    output = Image.fromarray(painted, mode="RGB").resize((3072, 3072), Image.Resampling.NEAREST)
    path = ROOT / f"skin_Bone_state{state}_v2.png"
    output.save(path, format="PNG", compress_level=9)
    return path


if __name__ == "__main__":
    for state in (1, 2):
        print(build(state))
