"""Build the pixel-faithful JadePhantom Night colourway sheets.

The approved sheets are the geometry source of truth.  Armour pixels are painted
through a discrete navy/cyan ramp; emissive, canopy, markings, ink, and damage
pixels remain from the approved sheets.
"""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
BG = np.array([0, 255, 0], dtype=np.uint8)


def armour_mask(rgb: np.ndarray) -> np.ndarray:
    r, g, b = [rgb[..., i].astype(np.int16) for i in range(3)]
    not_bg = np.any(rgb != BG, axis=2)
    # Jade plating is the green/teal material.  This excludes the blue canopy,
    # red/white diamonds, cream bars, gold marks, and warm damage effects.
    return not_bg & (g > r * 1.08) & (g >= b * 0.75) & ((g - r) > 8) & (g > 8)


def paint_night(rgb: np.ndarray) -> np.ndarray:
    out = rgb.copy()
    mask = armour_mask(rgb)
    r, g, b = [rgb[..., i].astype(np.int16) for i in range(3)]
    value = (0.299 * r + 0.587 * g + 0.114 * b).astype(np.int16)

    # Hard, deliberately stepped ramps keep the original pixel texture while
    # giving the material a painted Night treatment rather than a hue rotation.
    ramp = np.array(
        [
            [0x1A, 0x1F, 0x45],  # shadow
            [0x22, 0x28, 0x58],  # lower mid
            [0x2A, 0x2E, 0x6B],  # base
            [0x42, 0x52, 0x98],  # cool reflected plane
            [0x6E, 0xF2, 0xEE],  # cyan rim light
            [0xCF, 0xFB, 0xFA],  # sparse pale highlight
        ],
        dtype=np.uint8,
    )
    idx = np.select(
        [value < 28, value < 70, value < 118, value < 158, value < 198],
        [0, 1, 2, 3, 4],
        default=5,
    )
    out[mask] = ramp[idx[mask]]

    # The final approved cell is a bone-white hit flash.  Recolour its exposed
    # dark armour/shadow pixels, while leaving the bone-white flash, blue light,
    # warm effects, and true ink outline intact.
    hit = np.zeros(mask.shape, dtype=bool)
    hit[1024:1536, 1024:1536] = True
    dark = np.max(rgb, axis=2) < 120
    non_ink = np.any(rgb != 0, axis=2)
    cool_or_neutral = (b >= r * 0.8) & (b >= g * 0.8)
    blue_light = (b > 60) & (b > r * 1.15) & (b > g * 1.05)
    hit_armour = (
        hit
        & ~mask
        & dark
        & non_ink
        & cool_or_neutral
        & ~blue_light
        & np.any(rgb != BG, axis=2)
    )
    out[hit_armour] = ramp[1]

    # Background is an exact chroma-key green, including after all operations.
    out[np.all(rgb == BG, axis=2)] = BG
    return out


def main() -> None:
    for state in range(3):
        source = ROOT / f"skinref_state{state}.png"
        target = ROOT / f"skin_Night_state{state}.png"
        ref = np.asarray(Image.open(source).convert("RGB"))
        painted = paint_night(ref)
        enlarged = Image.fromarray(painted, "RGB").resize((3072, 3072), Image.Resampling.NEAREST)
        enlarged.save(target, format="PNG", optimize=False)
        print(target.name)


if __name__ == "__main__":
    main()
