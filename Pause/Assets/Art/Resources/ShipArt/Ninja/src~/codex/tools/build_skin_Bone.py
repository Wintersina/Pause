#!/usr/bin/env python3
"""Build the Bone colourway from the approved Ninja flipbook geometry.

The image-generation candidates in this folder supplied the ceramic ramp and
damage-treatment reference.  This cleanup pass deliberately takes geometry
from the approved sheets, repaints by material/value ramps, and upscales only
with nearest-neighbour so every pose keeps its exact silhouette and anchor.
"""

from __future__ import annotations

import colorsys
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
INK = (5, 6, 12)

# Warm ceramic ramp guided by the generated Bone candidates.  The explicit
# base/shadow/highlight colours are retained as named stops for reviewability.
BONE = (
    (76, 71, 63),       # deep ceramic shadow
    (112, 104, 91),     # shadow
    (185, 174, 152),    # requested shadow stop
    (219, 209, 188),    # mid-light
    (244, 234, 212),    # requested base
    (255, 250, 232),    # near-white highlight
)
SEAM = ((28, 27, 25), (61, 57, 51), (92, 88, 91))
RED_TIP = ((90, 12, 20), (134, 18, 31), (216, 35, 44), (255, 91, 69))
WRECK_METAL = ((17, 16, 15), (34, 31, 27), (57, 51, 43), (83, 73, 61))


def hsv(rgb: tuple[int, int, int]) -> tuple[float, float, float]:
    return colorsys.rgb_to_hsv(*(v / 255.0 for v in rgb)
    )


def value(rgb: tuple[int, int, int]) -> float:
    return (0.2126 * rgb[0] + 0.7152 * rgb[1] + 0.0722 * rgb[2]) / 255.0


def ramp_value(v: float, ramp: tuple[tuple[int, int, int], ...]) -> tuple[int, int, int]:
    if len(ramp) == 4:
        if v < 0.20:
            return ramp[0]
        if v < 0.42:
            return ramp[1]
        if v < 0.74:
            return ramp[2]
        return ramp[3]
    if v < 0.12:
        return ramp[0]
    if v < 0.28:
        return ramp[1]
    if v < 0.48:
        return ramp[2]
    if v < 0.70:
        return ramp[3]
    if v < 0.88:
        return ramp[4]
    return ramp[5]


def cell_center(cell: Image.Image) -> tuple[float, float]:
    points = [
        (x, y)
        for y in range(512)
        for x in range(512)
        if cell.getpixel((x, y)) != GREEN
    ]
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    return ((min(xs) + max(xs)) / 2.0, (min(ys) + max(ys)) / 2.0)


def in_radius(x: int, y: int, center: tuple[float, float], radius: float) -> bool:
    return (x - center[0]) ** 2 + (y - center[1]) ** 2 <= radius ** 2


def in_tip_zone(x: int, y: int, center: tuple[float, float]) -> bool:
    cx, cy = center
    return (
        (x > cx + 190 and abs(y - cy) < 34)
        or (x < cx - 190 and abs(y - cy) < 34)
        or (y < cy - 190 and abs(x - cx) < 34)
        or (y > cy + 190 and abs(x - cx) < 34)
    )


def is_blue_armour(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    h, s, v = hsv(rgb)
    # Indigo hull faces, including the very dark navy faces, but not cyan,
    # neutral gunmetal, or the warm damage pixels.
    return (
        0.56 <= h <= 0.73
        and s >= 0.16
        and b >= r + 8
        and b >= g + 3
        and v > 0.015
    )


def is_tip_red(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    h, s, v = hsv(rgb)
    return (h < 0.08 or h > 0.96) and s > 0.28 and r > g + 12 and r > b + 8 and v > 0.08


def is_forbidden_green(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    return g > 120 and g > r * 1.25 and g > b * 1.10


def is_pink_or_magenta(rgb: tuple[int, int, int]) -> bool:
    h, s, v = hsv(rgb)
    return v > 0.10 and s > 0.25 and 0.78 <= h <= 0.96


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

    if is_forbidden_green(rgb) or is_pink_or_magenta(rgb):
        return INK

    # Keep the gem region pixel-identical.  This preserves cyan animation,
    # the glint dashes, the lights-off gem, and the hit-flash gem exactly.
    if in_radius(x, y, center, 45.0):
        return rgb

    if hit_flash:
        # The approved hit frame is a bone-white flash. Preserve its dark
        # selective outline while rebuilding the flash with warm ceramic bands.
        if max(rgb) < 30:
            return INK
        return ramp_value(value(rgb), BONE)

    if in_tip_zone(x, y, center) and is_tip_red(rgb):
        return ramp_value(value(rgb), RED_TIP)

    if is_blue_armour(rgb):
        # A small directional warm lift makes this a material repaint rather
        # than a flat hue shift while keeping the source's hard pixel bands.
        v = value(rgb)
        if x < center[0] and y < center[1] and v > 0.45:
            v = min(1.0, v + 0.04)
        return ramp_value(v, BONE)

    # The octagonal hub is gunmetal, not ceramic.  This follows the armour
    # test so the blue armour collar around it is repainted, while the actual
    # neutral hub, sockets, and edge hardware stay gunmetal.
    if in_radius(x, y, center, 74.0):
        return rgb

    # Wreck machinery and burn scars often contain near-black blue pixels that
    # are part of the surviving armour face. Repaint only those with a clear
    # navy bias; leave exposed dark machinery and ember shapes intact.
    r, g, b = rgb
    if wreck:
        # Warm the remaining exposed machinery and outer dark hull into the
        # Bone family while retaining its exact pixel pattern. Strong
        # red/orange embers and cyan damage conduits remain emissive so every
        # spark, burn scar, and exposed cable still reads as damage.
        r, g, b = rgb
        cyan_damage = g > r + 18 and b > r + 20 and g > 70 and b > 70
        warm_damage = r > b + 10 and r > g + 6
        if not cyan_damage and not warm_damage:
            if max(rgb) < 120:
                return ramp_value(value(rgb), WRECK_METAL)
            if max(rgb) < 220:
                return ramp_value(value(rgb), BONE)

    return rgb


def build_sheet(state: int) -> None:
    source = Image.open(ROOT / f"skinref_state{state}.png").convert("RGB")
    result = Image.new("RGB", (3072, 3072), GREEN)
    for row in range(3):
        for col in range(3):
            box = (col * 512, row * 512, (col + 1) * 512, (row + 1) * 512)
            src_cell = source.crop(box)
            center = cell_center(src_cell)
            hit_flash = row == 2 and col == 2
            out_cell = Image.new("RGB", (512, 512), GREEN)
            src = src_cell.load()
            dst = out_cell.load()
            for y in range(512):
                for x in range(512):
                    dst[x, y] = repaint_pixel(src[x, y], x, y, center, hit_flash, state == 2)
            # Required export scale and cleanup filter: exactly nearest-neighbour.
            result.paste(out_cell.resize((1024, 1024), Image.Resampling.NEAREST), (col * 1024, row * 1024))
    result.save(ROOT / f"skin_Bone_state{state}.png", optimize=False)


def main() -> None:
    for state in range(3):
        build_sheet(state)


if __name__ == "__main__":
    main()
