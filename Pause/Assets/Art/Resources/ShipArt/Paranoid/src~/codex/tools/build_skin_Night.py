"""Build the Night repaint from the approved Paranoid flipbook references.

The source cells stay at 512 px while the material repaint is applied. The
finished 2x nearest-neighbour upscale is the final operation, so the supplied
3x3 sheets retain their exact silhouette, pose, and pixel edge structure.
"""

from __future__ import annotations

import colorsys
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
MAGENTA = (255, 0, 255)
INK = (5, 6, 12)
OUT_CELL = 1024
REF_CELL = 512

# Night's six-step armour ramp. The requested base, shadow, and cyan rim-light
# are kept as literal palette entries; the intermediate steps are deliberately
# hard bands rather than a continuous hue/saturation transform.
NIGHT_RAMP = (
    (26, 31, 69),       # #1A1F45 shadow
    (36, 41, 86),       # deep indigo
    (42, 46, 107),      # #2A2E6B base
    (57, 68, 133),      # panel mid
    (77, 101, 174),     # blue-violet light
    (110, 242, 238),    # #6EF2EE cyan rim light
)

# Bone remains bone-white in the hit-flash cell, but is re-stepped so that the
# flash is still a real paint pass and not an unchanged copied cell.
BONE_RAMP = (
    (132, 128, 122),
    (164, 159, 150),
    (194, 188, 177),
    (221, 213, 198),
    (244, 234, 212),
    (255, 249, 229),
)


def hsv(pixel: tuple[int, int, int]) -> tuple[float, float, float]:
    r, g, b = pixel
    return colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)


def canopy(pixel: tuple[int, int, int]) -> bool:
    r, g, b = pixel
    return r < 100 and g > 95 and b > 100 and b >= g * 0.88 and b > r * 1.6


def green_armour(pixel: tuple[int, int, int]) -> bool:
    """Catch diffuse green armour, while leaving glass, FX, and hardware alone."""
    if pixel == MAGENTA or canopy(pixel):
        return False
    h, s, _ = hsv(pixel)
    r, g, b = pixel
    # The lower hue bound catches the yellow-green highlights. The low-value
    # guard prevents dark neutral ink and engine hardware from being repainted.
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


def luminance(pixel: tuple[int, int, int]) -> float:
    r, g, b = pixel
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def ramp_colour(value: float, ramp: tuple[tuple[int, int, int], ...]) -> tuple[int, int, int]:
    # Six hard steps preserve the source's hand-placed pixel texture.
    lo, hi = 28.0, 220.0
    t = max(0.0, min(1.0, (value - lo) / (hi - lo)))
    index = min(len(ramp) - 1, int(round(t * (len(ramp) - 1))))
    return ramp[index]


def neighbour(cell: Image.Image, x: int, y: int, dx: int, dy: int) -> tuple[int, int, int]:
    nx = max(0, min(REF_CELL - 1, x + dx))
    ny = max(0, min(REF_CELL - 1, y + dy))
    return cell.getpixel((nx, ny))


def paint_night_cell(cell: Image.Image, row: int, col: int) -> Image.Image:
    source = cell.copy().convert("RGB")
    pixels = source.load()

    # The bottom-right pose is the approved hit-flash. Keep its bright bone
    # livery bone-white, with a fresh stepped material ramp.
    if row == 2 and col == 2:
        for y in range(REF_CELL):
            for x in range(REF_CELL):
                p = pixels[x, y]
                if bone_armour(p):
                    pixels[x, y] = ramp_colour(luminance(p), BONE_RAMP)
        return source

    original = source.copy()
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

            # Rebuild the plate edge placement: bright cyan belongs to the
            # upper-left-facing edge, while lower-right breaks fall into the
            # navy shadow ramp. This adds a distinct Night material response
            # without changing any shape, seam, damage, or silhouette pixel.
            upper_edge = (
                (not green_armour(up) and luminance(up) < value + 18)
                or (not green_armour(left) and luminance(left) < value + 18)
            )
            lower_edge = (
                (not green_armour(down) and luminance(down) <= value + 4)
                or (not green_armour(right) and luminance(right) <= value + 4)
            )

            if upper_edge and value >= 100:
                q = NIGHT_RAMP[5]
            elif upper_edge and value >= 72:
                q = NIGHT_RAMP[4]
            elif lower_edge and value < 95:
                q = NIGHT_RAMP[0]
            else:
                q = ramp_colour(value, NIGHT_RAMP[:5])

            # Sparse, deterministic one-pixel paint wear/rivet glints enrich
            # the new material while remaining inside already-painted armour.
            if value >= 105 and (x * 5 + y * 3 + row * 7 + col) % 29 == 0:
                q = NIGHT_RAMP[4]
            pixels[x, y] = q

    return source


def sanitize_dark_purple_ink(cell: Image.Image) -> None:
    """Replace near-black purple quantisation with the approved neutral ink."""
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
    source = Image.open(ROOT / f"skinref_state{state}.png").convert("RGB")
    sheet = Image.new("RGB", (OUT_CELL * 3, OUT_CELL * 3), MAGENTA)
    for row in range(3):
        for col in range(3):
            cell = source.crop((col * REF_CELL, row * REF_CELL, (col + 1) * REF_CELL, (row + 1) * REF_CELL))
            painted = paint_night_cell(cell, row, col)
            sanitize_dark_purple_ink(painted)
            painted = painted.resize((OUT_CELL, OUT_CELL), Image.Resampling.NEAREST)
            sheet.paste(painted, (col * OUT_CELL, row * OUT_CELL))
    sheet.save(ROOT / f"skin_Night_state{state}.png", format="PNG", optimize=False)


if __name__ == "__main__":
    for state in range(3):
        build(state)
        print(f"wrote skin_Night_state{state}.png")
