"""Build Lance damaged/wreck colourways from the approved reference sheets.

The intact Lance sheet is used only as a painted stripe/material donor.  The
approved damaged and wreck sheets remain the geometry/FX source of truth.
"""

from pathlib import Path

import colorsys

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)
CELL = 512
EXPORT_CELL = 1024

# Exact Lance racing-livery ramp found in the completed intact Lance sheet.
# The three requested anchor colours are present, with two intermediate pixel
# ramp steps retained for the hand-painted look.
STRIPE_COLORS = {
    (15, 94, 106),    # shadow #0F5E6A
    (19, 132, 143),   # intermediate shadow
    (31, 181, 185),   # base #1FB5B9
    (65, 216, 213),   # intermediate highlight
    (110, 242, 238),  # bright edge #6EF2EE
}


def load(path: Path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGB"), dtype=np.uint8)


def split_cells(sheet: np.ndarray) -> list[np.ndarray]:
    return [
        sheet[y : y + CELL, x : x + CELL]
        for y in range(0, 1536, CELL)
        for x in range(0, 1536, CELL)
    ]


def join_cells(cells: list[np.ndarray]) -> np.ndarray:
    rows = [np.concatenate(cells[row : row + 3], axis=1) for row in range(0, 9, 3)]
    return np.concatenate(rows, axis=0)


def blue_armour_mask(rgb: np.ndarray) -> np.ndarray:
    """Return blue/cyan armour pixels, excluding orange damage and gunmetal."""

    f = rgb.astype(np.float32) / 255.0
    flat = f.reshape(-1, 3)
    hsv = np.array([colorsys.rgb_to_hsv(*px) for px in flat], dtype=np.float32)
    hue = hsv[:, 0].reshape(rgb.shape[:2]) * 360.0
    sat = hsv[:, 1].reshape(rgb.shape[:2])
    value = hsv[:, 2].reshape(rgb.shape[:2])
    r, g, b = f[..., 0], f[..., 1], f[..., 2]
    return (
        np.any(rgb != GREEN, axis=2)
        & (hue >= 165.0)
        & (hue <= 285.0)
        & (sat >= 0.20)
        & (b >= r + 0.02)
        & (value > 0.08)
    )


def light_bone_mask(rgb: np.ndarray) -> np.ndarray:
    """Keep the hit-flash colour change within the bone-white ramp."""

    r, g, b = [rgb[..., i].astype(np.int16) for i in range(3)]
    return (r > 175) & (g > 175) & (b > 175) & ((r - b) < 55)


def dilate(mask: np.ndarray, radius: int = 1) -> np.ndarray:
    result = mask.copy()
    for _ in range(radius):
        result = result | np.roll(mask, 1, 0) | np.roll(mask, -1, 0)
        result = result | np.roll(mask, 1, 1) | np.roll(mask, -1, 1)
        mask = result
    return result


def damage_safe_surface_mask(rgb: np.ndarray) -> np.ndarray:
    """Allow stripe paint on substrate, not on bright tears or deep soot."""

    f = rgb.astype(np.float32) / 255.0
    flat = f.reshape(-1, 3)
    hsv = np.array([colorsys.rgb_to_hsv(*px) for px in flat], dtype=np.float32)
    hue = hsv[:, 0].reshape(rgb.shape[:2]) * 360.0
    saturation = hsv[:, 1].reshape(rgb.shape[:2])
    red = rgb[..., 0]
    dark_soot = rgb.max(axis=2) < 55
    bright_orange_tear = (
        (hue >= 10.0) & (hue <= 70.0) & (saturation > 0.25) & (red > 120)
    )
    return np.any(rgb != GREEN, axis=2) & ~dark_soot & ~bright_orange_tear


def nearest_stripe_labels(mask: np.ndarray, donor: np.ndarray) -> np.ndarray:
    """Propagate a donor stripe colour by a few nearest-neighbour pixels."""

    labels = np.full(mask.shape, -1, dtype=np.int16)
    palette = list(STRIPE_COLORS)
    for number, color in enumerate(palette):
        labels[mask & np.all(donor == np.array(color, dtype=np.uint8), axis=2)] = number
    for _ in range(2):
        old = labels.copy()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            shifted = np.roll(old, (dy, dx), axis=(0, 1))
            labels[(labels < 0) & (shifted >= 0)] = shifted[(labels < 0) & (shifted >= 0)]
    return labels


def main() -> None:
    reference_intact = load(ROOT / "skinref_state0.png")
    lance_intact = load(ROOT / "skin_Lance_state0.png")[::2, ::2]

    source_cells = split_cells(reference_intact)
    donor_cells = split_cells(lance_intact)

    stripe_masks: list[np.ndarray] = []
    flash_masks: list[np.ndarray] = []
    for source, donor in zip(source_cells, donor_cells):
        donor_colors = {tuple(px) for px in donor.reshape(-1, 3)}
        palette_mask = np.zeros(source.shape[:2], dtype=bool)
        for color in STRIPE_COLORS & donor_colors:
            palette_mask |= np.all(donor == np.array(color, dtype=np.uint8), axis=2)
        stripe_masks.append(np.any(source != donor, axis=2) & palette_mask)

        donor_flash = light_bone_mask(donor)
        source_flash = light_bone_mask(source)
        flash_masks.append(np.any(source != donor, axis=2) & donor_flash & source_flash)

    for state in (1, 2):
        source = load(ROOT / f"skinref_state{state}.png")
        out_cells = [cell.copy() for cell in split_cells(source)]
        for index, out in enumerate(out_cells):
            if index < 8:
                if state == 2:
                    stripe = dilate(stripe_masks[index], radius=1)
                    paint = stripe & damage_safe_surface_mask(out)
                    labels = nearest_stripe_labels(stripe_masks[index], donor_cells[index])
                    palette = np.array(list(STRIPE_COLORS), dtype=np.uint8)
                    for number, color in enumerate(palette):
                        out[paint & (labels == number)] = color
                else:
                    paint = stripe_masks[index] & blue_armour_mask(out)
                    out[paint] = donor_cells[index][paint]
            else:
                # The hit-flash remains bone-white; only its bone-white ramp
                # is borrowed, never its shape or any dark damage marks.
                paint = flash_masks[index] & light_bone_mask(out)
                out[paint] = donor_cells[index][paint]

        output = join_cells(out_cells)
        Image.fromarray(output, mode="RGB").resize(
            (3072, 3072), Image.Resampling.NEAREST
        ).save(ROOT / f"skin_Lance_state{state}_v2.png")


if __name__ == "__main__":
    main()
