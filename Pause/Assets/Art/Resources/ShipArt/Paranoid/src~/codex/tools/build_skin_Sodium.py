"""Build the Sodium repaint from the approved Paranoid flipbook references.

The generated Sodium candidates were used as the material target. This final
pass keeps the approved reference geometry pixel-locked: it repaints only the
green diffuse armour, then performs the required 2x nearest-neighbour export.
"""

from __future__ import annotations

import colorsys
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
MAGENTA = (255, 0, 255)
INK = (5, 6, 12)
REF_CELL = 512
OUT_CELL = 1024

# Sodium-lamp metal ramp, ordered from deep shadow to hot edge. These are hard
# bands rather than a hue-shifted copy of the green ramp.
SODIUM_RAMP = (
    (126, 50, 18),    # deep burnt-orange shadow
    (169, 72, 26),    # requested shadow anchor #A9481A
    (211, 96, 27),    # seam-side mid
    (242, 134, 43),   # requested base #F2862B
    (255, 180, 60),   # requested highlight #FFB43C
    (255, 241, 176),  # pale yellow-white hot edge
)

# The hit flash remains bone-white, but gets a fresh stepped repaint so it is
# not an unchanged copied cell.
BONE_RAMP = (
    (130, 126, 119),
    (166, 160, 149),
    (196, 188, 174),
    (221, 211, 194),
    (245, 235, 214),
    (255, 249, 229),
)


def hsv(pixel: tuple[int, int, int]) -> tuple[float, float, float]:
    r, g, b = pixel
    return colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)


def luminance(pixel: tuple[int, int, int]) -> float:
    r, g, b = pixel
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def canopy(pixel: tuple[int, int, int]) -> bool:
    r, g, b = pixel
    return r < 100 and g > 95 and b > 100 and b >= g * 0.88 and b > r * 1.6


def green_armour(pixel: tuple[int, int, int]) -> bool:
    """Catch diffuse green plating, leaving glass, FX and hardware intact."""
    if pixel == MAGENTA or canopy(pixel):
        return False
    r, g, b = pixel
    h, s, _ = hsv(pixel)
    return (
        0.105 <= h <= 0.50
        and s >= 0.16
        and g >= r * 0.92
        and g >= b * 0.88
        and g >= 22
    )


def bone_armour(pixel: tuple[int, int, int]) -> bool:
    r, g, b = pixel
    return pixel != MAGENTA and max(pixel) - min(pixel) <= 58 and r >= 128 and g >= 122 and b >= 108


def ramp_colour(value: float, ramp: tuple[tuple[int, int, int], ...]) -> tuple[int, int, int]:
    lo, hi = 24.0, 228.0
    t = max(0.0, min(1.0, (value - lo) / (hi - lo)))
    return ramp[min(len(ramp) - 1, int(round(t * (len(ramp) - 1))))]


def paint_sodium_cell(cell: Image.Image, row: int, col: int) -> Image.Image:
    source = cell.convert("RGB")
    original = source.copy()
    pixels = source.load()

    # The bottom-right pose is the approved bone-white hit flash.
    if row == 2 and col == 2:
        for y in range(REF_CELL):
            for x in range(REF_CELL):
                p = original.getpixel((x, y))
                if bone_armour(p):
                    value = luminance(p)
                    # Preserve the bone-white family while adding a warm,
                    # stepped highlight response.
                    pixels[x, y] = ramp_colour(value, BONE_RAMP)
        return source

    for y in range(REF_CELL):
        for x in range(REF_CELL):
            p = original.getpixel((x, y))
            if not green_armour(p):
                continue

            value = luminance(p)
            up = original.getpixel((x, max(0, y - 1)))
            left = original.getpixel((max(0, x - 1), y))
            down = original.getpixel((x, min(REF_CELL - 1, y + 1)))
            right = original.getpixel((min(REF_CELL - 1, x + 1), y))

            # Re-place the Sodium key light on plate edges: top-left edges
            # receive hot orange, while lower-right edges fall into burnt
            # orange. This makes the pass a material repaint rather than a
            # channel-wise hue rotation.
            upper_edge = (
                (not green_armour(up) and luminance(up) < value + 20)
                or (not green_armour(left) and luminance(left) < value + 20)
            )
            lower_edge = (
                (not green_armour(down) and luminance(down) <= value + 5)
                or (not green_armour(right) and luminance(right) <= value + 5)
            )

            if upper_edge and value >= 104:
                q = SODIUM_RAMP[5]
            elif upper_edge and value >= 72:
                q = SODIUM_RAMP[4]
            elif lower_edge and value < 92:
                q = SODIUM_RAMP[0]
            else:
                q = ramp_colour(value, SODIUM_RAMP[:5])

            # Deterministic sparse paint wear and rivet glints are placed only
            # inside armour pixels, so damage, seams and the silhouette stay
            # exactly as approved.
            if value >= 112 and (x * 5 + y * 3 + row * 7 + col) % 31 == 0:
                q = SODIUM_RAMP[4]
            pixels[x, y] = q

    return source


def sanitize_dark_purple_ink(cell: Image.Image) -> None:
    pixels = cell.load()
    for y in range(REF_CELL):
        for x in range(REF_CELL):
            p = pixels[x, y]
            if p == MAGENTA:
                continue
            h, s, v = hsv(p)
            if 280.0 / 360.0 <= h <= 345.0 / 360.0 and s > 0.5 and v <= 0.25:
                pixels[x, y] = INK


def build(state: int) -> None:
    reference = Image.open(ROOT / f"skinref_state{state}.png").convert("RGB")
    sheet = Image.new("RGB", (OUT_CELL * 3, OUT_CELL * 3), MAGENTA)
    for row in range(3):
        for col in range(3):
            cell = reference.crop((col * REF_CELL, row * REF_CELL, (col + 1) * REF_CELL, (row + 1) * REF_CELL))
            painted = paint_sodium_cell(cell, row, col)
            sanitize_dark_purple_ink(painted)
            painted = painted.resize((OUT_CELL, OUT_CELL), Image.Resampling.NEAREST)
            sheet.paste(painted, (col * OUT_CELL, row * OUT_CELL))
    sheet.save(ROOT / f"skin_Sodium_state{state}.png", format="PNG", optimize=False)


if __name__ == "__main__":
    for state in range(3):
        build(state)
        print(f"wrote skin_Sodium_state{state}.png")
