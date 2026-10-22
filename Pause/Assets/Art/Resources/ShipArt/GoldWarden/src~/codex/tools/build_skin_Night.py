#!/usr/bin/env python3
"""Build the Night colourway while locking every reference silhouette and pose."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)
REFERENCES = tuple(ROOT / f"skinref_state{i}.png" for i in range(3))
OUTPUTS = tuple(ROOT / f"skin_Night_state{i}.png" for i in range(3))


def hsv(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Vectorised RGB-to-HSV, with hue in degrees and RGB in 0..1."""
    value = rgb.max(axis=2)
    minimum = rgb.min(axis=2)
    delta = value - minimum
    saturation = np.divide(delta, value, out=np.zeros_like(value), where=value != 0)
    hue = np.zeros_like(value)
    nonzero = delta != 0
    red, green, blue = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    red_case = nonzero & (value == red)
    green_case = nonzero & (value == green) & ~red_case
    blue_case = nonzero & (value == blue) & ~red_case & ~green_case
    hue[red_case] = ((green[red_case] - blue[red_case]) / delta[red_case]) % 6
    hue[green_case] = (blue[green_case] - red[green_case]) / delta[green_case] + 2
    hue[blue_case] = (red[blue_case] - green[blue_case]) / delta[blue_case] + 4
    return hue * 60.0, saturation, value


def shifted(mask: np.ndarray, dy: int, dx: int) -> np.ndarray:
    """Zero-padded neighbour lookup, avoiding wraparound at cell edges."""
    result = np.zeros_like(mask)
    source_y0 = max(0, -dy)
    source_y1 = mask.shape[0] - max(0, dy)
    source_x0 = max(0, -dx)
    source_x1 = mask.shape[1] - max(0, dx)
    target_y0 = max(0, dy)
    target_y1 = mask.shape[0] - max(0, -dy)
    target_x0 = max(0, dx)
    target_x1 = mask.shape[1] - max(0, -dx)
    result[target_y0:target_y1, target_x0:target_x1] = mask[source_y0:source_y1, source_x0:source_x1]
    return result


def make_sheet(reference_path: Path, output_path: Path) -> None:
    reference = np.asarray(Image.open(reference_path).convert("RGB"), dtype=np.uint8)
    if reference.shape != (1536, 1536, 3):
        raise ValueError(f"{reference_path.name}: expected 1536x1536 RGB")

    rgb = reference.astype(np.float32) / 255.0
    hue, saturation, value = hsv(rgb)
    red, green, blue = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    non_green = np.any(reference != GREEN, axis=2)

    # The lower-right cell is the approved bone-white hit flash and is intentionally immutable.
    hit_flash = np.zeros(non_green.shape, dtype=bool)
    hit_flash[1024:1536, 1024:1536] = True
    editable = non_green & ~hit_flash

    # Gold plate pixels are warm and red/green dominant. The second clause recovers the
    # dark-brown plate shading without pulling in the blue gunmetal or teal canopy.
    warm_plate = (
        editable
        & (hue >= 27.0)
        & (hue <= 72.0)
        & (saturation > 0.08)
        & (value > 0.025)
        & (red >= blue)
        & (green >= blue)
    )
    dark_plate = (
        editable
        & (red >= blue)
        & (green >= blue)
        & (red > blue + 0.008)
        & (green > blue + 0.004)
        & (value > 0.08)
    )
    explicit_gold = editable & (hue >= 35.0) & (hue <= 55.0) & (saturation > 0.50)
    armor = warm_plate | dark_plate | explicit_gold

    # A one-pixel expansion catches the dark material under plate seams while excluding
    # blue gunmetal, canopy glass, red markings, and low-value ink.
    near_armor = armor | shifted(armor, 1, 0) | shifted(armor, -1, 0) | shifted(armor, 0, 1) | shifted(armor, 0, -1)
    near_armor = (
        near_armor
        | shifted(near_armor, 1, 1)
        | shifted(near_armor, 1, -1)
        | shifted(near_armor, -1, 1)
        | shifted(near_armor, -1, -1)
    )
    near_armor = (
        near_armor
        | shifted(near_armor, 1, 0)
        | shifted(near_armor, -1, 0)
        | shifted(near_armor, 0, 1)
        | shifted(near_armor, 0, -1)
    )
    structural_plate = (
        near_armor
        & editable
        & (red >= blue)
        & (green >= blue)
        & (value > 0.050)
        & ~((red > green * 1.45) & (red > blue * 1.45))
    )
    # Wreck cells lose much of their warm surface to damage. A small neutral shadow
    # fringe around surviving plates keeps the colourway visibly present without
    # touching the blue canopy/gunmetal or warm damage pixels.
    blue_region = (
        (((hue >= 150.0) & (hue <= 270.0)) | ((hue >= 170.0) & (hue <= 230.0)))
        & (saturation > 0.16)
        & (value > 0.08)
    )
    red_mark = ((hue < 20.0) | (hue > 345.0)) & (saturation > 0.30)
    warm_damage = (hue >= 15.0) & (hue < 28.0) & (saturation > 0.45)
    neutral_shadow = (
        near_armor
        & editable
        & (value > 0.030)
        & (value < 0.45)
        & ~blue_region
        & ~red_mark
        & ~warm_damage
        & ~((red > green * 1.45) & (red > blue * 1.45))
    )
    armor |= structural_plate | neutral_shadow

    # Hard stepped Night ramp. This is a new material ramp, not a hue rotation.
    luminance = 0.299 * red + 0.587 * green + 0.114 * blue
    ramp = np.array(
        [
            [26, 31, 69],    # shadow #1A1F45
            [31, 37, 82],
            [42, 46, 107],   # base #2A2E6B
            [55, 70, 137],
            [83, 109, 168],
        ],
        dtype=np.uint8,
    )
    bands = np.select(
        [luminance < 0.20, luminance < 0.34, luminance < 0.50, luminance < 0.68],
        [0, 1, 2, 3],
        default=4,
    )
    repaint = ramp[bands]
    result = reference.copy()
    result[armor] = repaint[armor]

    # Cyan edge light is restricted to bright upper-left plate boundaries so panel seams
    # and the original selective outline remain readable.
    upper_left_boundary = armor & (~shifted(armor, 1, 0) | ~shifted(armor, 0, 1))
    rim = armor & upper_left_boundary & (luminance > 0.44) & (saturation > 0.08)
    result[rim] = np.array([110, 242, 238], dtype=np.uint8)  # #6EF2EE

    pale = armor & (luminance > 0.88) & ~rim
    result[pale] = np.array([214, 250, 255], dtype=np.uint8)

    # Reassert the key colour and approved flash cell before nearest-neighbour export.
    result[~non_green] = GREEN
    result[hit_flash] = reference[hit_flash]
    enlarged = Image.fromarray(result, mode="RGB").resize((3072, 3072), Image.Resampling.NEAREST)
    enlarged.save(output_path, format="PNG", optimize=False)


def main() -> int:
    for reference, output in zip(REFERENCES, OUTPUTS):
        make_sheet(reference, output)
        print(f"wrote {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
