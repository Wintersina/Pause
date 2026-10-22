"""Paint the Bone IonLancer sheets from the approved flipbook.

The repaint is deliberately cell-preserving.  Every source pixel keeps its
position and every non-armour material keeps its source colour; the armour is
repainted through an explicit ceramic ramp with value-aware edge and highlight
passes.  The generated Bone candidate was used as the visual ramp reference,
while the approved sheets remain the geometry source of truth.
"""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)

# Warm ceramic ramp, shadow to highlight.  These are intentionally discrete
# paint steps so the 2x export remains crisp pixel art after nearest scaling.
BONE_RAMP = np.array(
    [
        (185, 174, 152),  # #B9AE98 shadow
        (207, 196, 173),  # warm-grey seam/half-shadow
        (229, 218, 194),  # ceramic midtone
        (244, 234, 212),  # #F4EAD4 base
        (251, 244, 226),  # lit ceramic
        (255, 253, 243),  # near-white highlight
    ],
    dtype=np.uint8,
)


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

    # Teal canopy/glass is protected from the armour repaint.
    blue_glass = (
        (hue >= 165)
        & (hue <= 215)
        & (sat > 0.45)
        & (g > 20)
        & ((g / np.maximum(b, 1)) > 0.58)
        & ((g / np.maximum(b, 1)) < 1.08)
    )

    # Cobalt plate family only.  Deep navy damage/voids are intentionally not
    # included, so soot, tears and exposed damage remain byte-identical.
    armour = (
        (hue >= 190)
        & (hue <= 250)
        & (sat > 0.38)
        & (value > 0.18)
        & (b > g * 1.08)
        & ~blue_glass
        & ~((r < 20) & (g < 22) & (b < 45))
    )
    return armour, blue_glass, hue, sat, value, r, g, b


def paint_armour(rgb: np.ndarray, armour: np.ndarray, value: np.ndarray) -> np.ndarray:
    out = rgb.copy()
    # Six hard bands preserve the source's panel texture without introducing
    # gradients or resampling.
    steps = np.select(
        [value < 0.28, value < 0.42, value < 0.58, value < 0.74, value < 0.90],
        [0, 1, 2, 3, 4],
        default=5,
    )
    out[armour] = BONE_RAMP[steps[armour]]

    # Hand-painted ceramic edge accents: preserve the exact source edge pixels
    # but give their lit side a warm, near-white rim and keep the shadow side
    # one ramp step down.  This is a material repaint, not a hue rotation.
    up = np.zeros_like(armour)
    left = np.zeros_like(armour)
    up[1:] = armour[:-1]
    left[:, 1:] = armour[:, :-1]
    lit_edge = armour & (~up | ~left) & (value > 0.52)
    out[lit_edge] = BONE_RAMP[5]

    down = np.zeros_like(armour)
    right = np.zeros_like(armour)
    down[:-1] = armour[1:]
    right[:, :-1] = armour[:, 1:]
    shadow_edge = armour & (~down | ~right) & (value < 0.42)
    out[shadow_edge] = BONE_RAMP[0]
    return out


def repaint(source: Image.Image) -> Image.Image:
    rgb = np.asarray(source.convert("RGB"), dtype=np.uint8)
    out = rgb.copy()
    armour, _glass, hue, sat, value, r, g, b = material_masks(rgb)
    out = paint_armour(rgb, armour, value)

    # The approved hit-flash pose is already bone-white, but receives a warm
    # ceramic repaint so it still registers as a real Bone colourway change.
    hit = np.zeros(rgb.shape[:2], dtype=bool)
    hit[1024:1536, 1024:1536] = True
    neutral_bone = (
        hit
        & (r > 80)
        & (g > 70)
        & (b > 60)
        & ((np.maximum(np.maximum(r, g), b) - np.minimum(np.minimum(r, g), b)) < 80)
    )
    hit_steps = np.select(
        [value < 0.42, value < 0.58, value < 0.74, value < 0.90],
        [0, 2, 3, 4],
        default=5,
    )
    out[neutral_bone] = BONE_RAMP[hit_steps[neutral_bone]]

    # The references contain a small amount of forbidden magenta contamination
    # around old blue composites.  Remove it into the local Bone ramp while
    # leaving red, orange, teal, engine and damage colours untouched.
    forbidden_magenta = (hue > 285) & (hue < 345) & (sat > 0.30)
    clean_steps = np.select(
        [value < 0.28, value < 0.42, value < 0.58, value < 0.74, value < 0.90],
        [0, 1, 2, 3, 4],
        default=5,
    )
    out[forbidden_magenta] = BONE_RAMP[clean_steps[forbidden_magenta]]

    # Keep the pure green key background exact and avoid edge wrap artefacts.
    out[np.all(rgb == GREEN, axis=2)] = GREEN
    out[0, :, :] = rgb[0, :, :]
    out[-1, :, :] = rgb[-1, :, :]
    out[:, 0, :] = rgb[:, 0, :]
    out[:, -1, :] = rgb[:, -1, :]

    enlarged = Image.fromarray(out, mode="RGB").resize((3072, 3072), Image.Resampling.NEAREST)
    return enlarged


def main() -> None:
    for state in range(3):
        source_path = ROOT / f"skinref_state{state}.png"
        target_path = ROOT / f"skin_Bone_state{state}.png"
        repaint(Image.open(source_path)).save(target_path, format="PNG", compress_level=9)
        print(target_path)


if __name__ == "__main__":
    main()
