#!/usr/bin/env python3
"""Build the Ronin repaint from the approved Ninja flipbook sheets.

The generator pass is used as the colourway reference; this cleanup pass keeps
the approved raster geometry locked and only repaints material pixels.
"""

from __future__ import annotations

import colorsys
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
INK = (5, 6, 12)
INDIGO = ((35, 42, 110), (74, 88, 200), (159, 168, 240))
RED = ((134, 18, 31), (216, 35, 44), (255, 91, 69))
BONE = ((106, 102, 95), (180, 175, 163), (244, 234, 212), (255, 250, 232))


def hsv(rgb: tuple[int, int, int]) -> tuple[float, float, float]:
    return colorsys.rgb_to_hsv(*(v / 255.0 for v in rgb))


def is_blue_armour(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    # The approved ship's armour is the blue/indigo material. This excludes
    # the neutral hub, cyan gem, warm damage, and pure outline pixels.
    h, s, v = hsv(rgb)
    return 0.54 <= h <= 0.72 and s >= 0.18 and b > r + 20 and b > g + 8 and v > 0.08


def ramp_value(rgb: tuple[int, int, int]) -> float:
    r, g, b = rgb
    return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0


def material_ramp(value: float, ramp: tuple[tuple[int, int, int], ...]) -> tuple[int, int, int]:
    if len(ramp) == 3:
        if value < 0.30:
            return ramp[0]
        if value < 0.66:
            return ramp[1]
        return ramp[2]
    if value < 0.20:
        return ramp[0]
    if value < 0.42:
        return ramp[1]
    if value < 0.74:
        return ramp[2]
    return ramp[3]


def cell_center(cell: Image.Image) -> tuple[float, float]:
    pix = cell.load()
    points = [(x, y) for y in range(512) for x in range(512) if pix[x, y] != GREEN]
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    return ((min(xs) + max(xs)) / 2.0, (min(ys) + max(ys)) / 2.0)


def in_hub_or_gem(x: int, y: int, center: tuple[float, float]) -> bool:
    dx = x - center[0]
    dy = y - center[1]
    return dx * dx + dy * dy <= 74.0 * 74.0


def in_gem(x: int, y: int, center: tuple[float, float]) -> bool:
    dx = x - center[0]
    dy = y - center[1]
    return dx * dx + dy * dy <= 45.0 * 45.0


def in_tip_zone(x: int, y: int, center: tuple[float, float]) -> bool:
    cx, cy = center
    return (
        (x > cx + 190 and abs(y - cy) < 34)
        or (y < cy - 190 and abs(x - cx) < 34)
        or (y > cy + 190 and abs(x - cx) < 34)
    )


def repaint_pixel(
    rgb: tuple[int, int, int],
    x: int,
    y: int,
    center: tuple[float, float],
    hit_flash: bool,
    wreck: bool,
) -> tuple[int, int, int]:
    if rgb == GREEN:
        return GREEN
    if in_hub_or_gem(x, y, center):
        return rgb
    if hit_flash:
        # Keep the flash neutral and bone-white, while retaining the original
        # hard seams and the locked central hub/gem.
        if max(rgb) < 28:
            return INK
        return material_ramp(ramp_value(rgb), BONE)

    if is_blue_armour(rgb):
        right_half = x > center[0]
        repaint_ramp = RED if right_half else INDIGO
        return material_ramp(ramp_value(rgb), repaint_ramp)

    # The wreck sheet contains large areas of nearly-black exposed structure.
    # Give those surviving material planes a Ronin ramp too; the burn shapes,
    # seams, tears, and machinery stay in their original pixels.
    if wreck and 28 <= max(rgb) < 100:
        right_half = x > center[0]
        repaint_ramp = RED if right_half else INDIGO
        return repaint_ramp[0] if ramp_value(rgb) < 0.20 else repaint_ramp[1]

    # Red-armour tip lamps read bone-white with a red-hot core. Keep the
    # left blade's original red-orange lamps unchanged.
    if x > center[0] and in_tip_zone(x, y, center):
        r, g, b = rgb
        if r > b + 18 and r > g + 22:
            return RED[1] if ramp_value(rgb) < 0.42 else BONE[2]

    # A few near-green wreck pixels exist in the source's burn/scorch detail;
    # they are not background and must not leak green into the ship.
    r, g, b = rgb
    if g > 120 and g > r * 1.25 and g > b * 1.10:
        return INK
    return rgb


def build_sheet(state: int) -> None:
    source = Image.open(ROOT / f"skinref_state{state}.png").convert("RGB")
    result = Image.new("RGB", (3072, 3072), GREEN)
    for row in range(3):
        for col in range(3):
            box = (col * 512, row * 512, (col + 1) * 512, (row + 1) * 512)
            cell = source.crop(box)
            center = cell_center(cell)
            hit_flash = row == 2 and col == 2
            out = Image.new("RGB", (512, 512), GREEN)
            src = cell.load()
            dst = out.load()
            for y in range(512):
                for x in range(512):
                    dst[x, y] = repaint_pixel(src[x, y], x, y, center, hit_flash, state == 2)
            result.paste(out.resize((1024, 1024), Image.Resampling.NEAREST), (col * 1024, row * 1024))
    result.save(ROOT / f"skin_Ronin_state{state}.png", optimize=False)


def main() -> None:
    for state in range(3):
        build_sheet(state)


if __name__ == "__main__":
    main()
