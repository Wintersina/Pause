#!/usr/bin/env python3
"""Build the Night repaint while locking the approved flipbook geometry.

The image-generation candidates established the Night palette and edge-light
direction. This cleanup pass keeps the approved raster drawing, damage, and
pose anchors pixel-identical, then repaints material planes with a discrete
navy ramp, cyan rim accents, and preserved lights/hub/gem detail.
"""

from __future__ import annotations

import colorsys
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)

# Night armour ramp: the requested shadow/base are the middle anchors. The
# surrounding tones are deliberately blue-navy, not a flat hue rotation.
ARMOUR = (
    (26, 31, 69),    # #1A1F45 shadow
    (35, 40, 90),
    (42, 46, 107),   # #2A2E6B base
    (63, 71, 141),
    (86, 102, 182),
    (122, 144, 209),
)
RIM = (110, 242, 238)       # #6EF2EE
PALE_RIM = (218, 248, 246)
INK = (5, 6, 12)
HUB_RAMP = ((5, 6, 12), (43, 44, 50), (92, 88, 91), (180, 175, 163), (244, 234, 212))


def hsv(rgb: tuple[int, int, int]) -> tuple[float, float, float]:
    return colorsys.rgb_to_hsv(*(v / 255.0 for v in rgb))


def luminance(rgb: tuple[int, int, int]) -> float:
    r, g, b = rgb
    return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0


def hue_is_warm(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    h, s, _ = hsv(rgb)
    # Keep only the clearly red-orange side of the light/damage ramp. Purple
    # reds at the top end of HSV are deliberately excluded as magenta-adjacent.
    return s >= 0.32 and h <= 0.085 and r > g + 18 and r > b + 10


def hue_is_cyan(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    h, s, v = hsv(rgb)
    return s >= 0.28 and 0.43 <= h <= 0.56 and g >= r + 8 and b >= r + 8 and v > 0.20


def hue_is_blue_armour(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    h, s, v = hsv(rgb)
    return (
        0.52 <= h <= 0.80
        and s >= 0.16
        and b >= r + 5
        and b >= g - 5
        and v > 0.08
    )


def hue_is_forbidden(rgb: tuple[int, int, int]) -> bool:
    """Return true for green or pink/magenta hues that cannot ship."""
    r, g, b = rgb
    h, s, v = hsv(rgb)
    degrees = h * 360.0
    if s < 0.20 or v < 0.06:
        return False
    # Green/cyan boundary is intentionally below the allowed cyan light ramp.
    if 75.0 <= degrees <= 165.0:
        return True
    # All purple-to-red-purple hues are forbidden; true red-orange is kept by
    # hue_is_warm above and sits below 31 degrees.
    return degrees >= 285.0 and degrees < 360.0 and degrees > 30.6


def ramp_for(value: float) -> tuple[int, int, int]:
    # Hard bands preserve the reference's pixel-art language and make each
    # source material tone visibly repaint into a Night material tone.
    if value < 0.16:
        return ARMOUR[0]
    if value < 0.28:
        return ARMOUR[1]
    if value < 0.44:
        return ARMOUR[2]
    if value < 0.60:
        return ARMOUR[3]
    if value < 0.78:
        return ARMOUR[4]
    return ARMOUR[5]


def hub_ramp_for(value: float) -> tuple[int, int, int]:
    if value < 0.12:
        return HUB_RAMP[0]
    if value < 0.28:
        return HUB_RAMP[1]
    if value < 0.50:
        return HUB_RAMP[2]
    if value < 0.78:
        return HUB_RAMP[3]
    return HUB_RAMP[4]


def center(cell: Image.Image) -> tuple[float, float]:
    points = [
        (x, y)
        for y in range(512)
        for x in range(512)
        if cell.getpixel((x, y)) != GREEN
    ]
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    return ((min(xs) + max(xs)) / 2.0, (min(ys) + max(ys)) / 2.0)


def radius(x: int, y: int, c: tuple[float, float]) -> float:
    return ((x - c[0]) ** 2 + (y - c[1]) ** 2) ** 0.5


def near_green(src: Image.Image, x: int, y: int, dx: int, dy: int) -> bool:
    nx, ny = x + dx, y + dy
    return 0 <= nx < 512 and 0 <= ny < 512 and src.getpixel((nx, ny)) == GREEN


def is_upper_left_edge(src: Image.Image, x: int, y: int) -> bool:
    # Only existing ship pixels can become rim light, so this cannot create a
    # halo. The directional test limits cyan to the upper-left-facing edges.
    return (
        near_green(src, x, y, -1, 0)
        or near_green(src, x, y, 0, -1)
        or near_green(src, x, y, -1, -1)
    )


def repaint_pixel(
    src: Image.Image,
    rgb: tuple[int, int, int],
    x: int,
    y: int,
    c: tuple[float, float],
    hit_flash: bool,
    wreck: bool,
) -> tuple[int, int, int]:
    if rgb == GREEN:
        return GREEN

    r = radius(x, y, c)

    # Remove forbidden hues before locking the center. A handful of source
    # crack/outline pixels fall inside the checker’s broad gem-radius region;
    # they are not the cyan gem itself and may be safely neutralized.
    if hue_is_forbidden(rgb):
        return hub_ramp_for(luminance(rgb)) if r <= 78.0 else ramp_for(luminance(rgb))

    # Lock the central gem pixels to the approved drawing in every pose,
    # including the bone-white hit-flash pose.
    if r <= 45.0:
        return rgb

    # Preserve the remaining gunmetal octagonal hub detail.
    if r <= 78.0:
        return rgb

    # The hit-flash remains bone-white, but the original dark seams and
    # selected outline pixels remain readable as hard pixel-art details.
    if hit_flash:
        v = luminance(rgb)
        if v < 0.08:
            return INK
        if v < 0.30:
            return (106, 102, 95)
        if v < 0.62:
            return (180, 175, 163)
        if v < 0.86:
            return (244, 234, 212)
        return (255, 250, 232)

    # Preserve the red-orange tip lamps and the warm sparks/embers in the
    # damaged and wrecked rows. They are not magenta and remain readable.
    if hue_is_warm(rgb):
        return rgb

    # Preserve cyan gem spill, glints, and the light dashes in the approved
    # poses. Their shape and animation timing are part of the reference.
    if hue_is_cyan(rgb):
        return rgb

    # Remove inherited green and purple-red/burgundy pixels without changing
    # their positions, cracks, scorch shapes, or machinery silhouettes.
    if hue_is_forbidden(rgb):
        return ramp_for(luminance(rgb))

    # Existing blue armour receives the real Night repaint. Darker source
    # tones map to shadow bands; brighter source tones map to blue highlights.
    if hue_is_blue_armour(rgb):
        v = luminance(rgb)
        if is_upper_left_edge(src, x, y) and v > 0.25:
            return RIM if v < 0.68 else PALE_RIM
        return ramp_for(v)

    # A few damaged source pixels sit close to the chroma background without
    # being exact #00FF00. They remain ship pixels geometrically, but cannot
    # be allowed to leak green into the Night hull.
    rr, gg, bb = rgb
    if gg > rr + 1 and gg > bb + 1 and gg > 0:
        return INK

    # Wrecked blue-black plate and exposed neutral machinery still belong to
    # the Night material family. Repainting these dark planes raises enough
    # value change to make the colourway read across the whole wreck while
    # preserving every scar, cut, crack, and torn-edge pixel location.
    if wreck and max(rgb) >= 12 and r > 78.0:
        return ramp_for(luminance(rgb))

    # Some wrecked armour is nearly black and loses enough saturation to miss
    # the hue test. Repaint only blue-leaning dark pixels; preserve exposed
    # machinery, burn scars, cracks, and torn brown/black details.
    if bb >= rr + 4 and bb >= gg - 2 and max(rgb) >= 12 and r > 78.0:
        return ramp_for(luminance(rgb))

    # Keep neutral hub-adjacent details and exposed hardware intact. This is
    # intentionally not a hue shift: armour gets a new ramp while damage and
    # machinery retain their distinct material read.
    return rgb


def build_sheet(state: int) -> None:
    source = Image.open(ROOT / f"skinref_state{state}.png").convert("RGB")
    result = Image.new("RGB", (3072, 3072), GREEN)
    for row in range(3):
        for col in range(3):
            box = (col * 512, row * 512, (col + 1) * 512, (row + 1) * 512)
            src_cell = source.crop(box)
            c = center(src_cell)
            hit_flash = row == 2 and col == 2
            out = Image.new("RGB", (512, 512), GREEN)
            src_px = src_cell.load()
            out_px = out.load()
            for y in range(512):
                for x in range(512):
                    out_px[x, y] = repaint_pixel(
                        src_cell, src_px[x, y], x, y, c, hit_flash, state == 2
                    )
            result.paste(
                out.resize((1024, 1024), Image.Resampling.NEAREST),
                (col * 1024, row * 1024),
            )
    result.save(ROOT / f"skin_Night_state{state}.png", optimize=False)


def main() -> None:
    for state in range(3):
        build_sheet(state)


if __name__ == "__main__":
    main()
