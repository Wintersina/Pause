"""Build the Bone repaint from the approved Paranoid flipbook references.

The three generated Bone candidates were used as the material target. This
final pass locks every cell to the approved reference geometry, repaints only
the diffuse armour, and exports each cell with a 2x nearest-neighbour scale.
That keeps pose, damage, canopy, glints, and the pure-magenta key unchanged.
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

# Warm ceramic ramp, ordered from shadow to hot edge. The ramp is deliberately
# stepped and contains the requested base/shadow anchors rather than applying
# a hue rotation to the green source.
BONE_RAMP = (
    (185, 174, 152),  # #B9AE98 warm ceramic shadow
    (204, 194, 177),
    (224, 214, 196),
    (244, 234, 212),  # #F4EAD4 ceramic base
    (255, 249, 229),
    (255, 253, 242),  # near-white highlight
)

RED_LIGHT = (216, 35, 44)   # #D8232C
RED_HOT = (255, 91, 69)     # #FF5B45


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
    """Catch diffuse green plating, leaving damage and hardware untouched."""
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
    """Recognise the already bone-white hit-flash hull without glass/ink."""
    if pixel == MAGENTA or canopy(pixel):
        return False
    r, g, b = pixel
    return max(pixel) - min(pixel) <= 58 and r >= 128 and g >= 122 and b >= 108


def ramp_colour(value: float, ramp: tuple[tuple[int, int, int], ...]) -> tuple[int, int, int]:
    lo, hi = 24.0, 232.0
    t = max(0.0, min(1.0, (value - lo) / (hi - lo)))
    index = min(len(ramp) - 1, int(round(t * (len(ramp) - 1))))
    return ramp[index]


def paint_bone_cell(cell: Image.Image, row: int, col: int) -> Image.Image:
    source = cell.convert("RGB")
    original = source.copy()
    pixels = source.load()

    # The bottom-right pose is the approved hit flash. It remains bone-white,
    # but receives a fresh warm ceramic ramp so it is not an unchanged copy.
    if row == 2 and col == 2:
        for y in range(REF_CELL):
            for x in range(REF_CELL):
                p = original.getpixel((x, y))
                if bone_armour(p):
                    pixels[x, y] = ramp_colour(luminance(p), BONE_RAMP)
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

            # Re-place the ceramic key light on plate edges: top-left faces
            # get near-white ceramic, while lower-right edges fall to the
            # warm-grey shadow. This is a material repaint, not a hue shift.
            upper_edge = (
                (not green_armour(up) and luminance(up) < value + 18)
                or (not green_armour(left) and luminance(left) < value + 18)
            )
            lower_edge = (
                (not green_armour(down) and luminance(down) <= value + 4)
                or (not green_armour(right) and luminance(right) <= value + 4)
            )

            if upper_edge and value >= 100:
                q = BONE_RAMP[5]
            elif upper_edge and value >= 72:
                q = BONE_RAMP[4]
            elif lower_edge and value < 95:
                q = BONE_RAMP[0]
            else:
                q = ramp_colour(value, BONE_RAMP[:5])

            # Sparse deterministic ceramic wear and rivet glints. They stay
            # within existing armour pixels and cannot alter the silhouette.
            if value >= 108 and (x * 5 + y * 3 + row * 7 + col) % 29 == 0:
                q = BONE_RAMP[4]
            pixels[x, y] = q

    return source


def sanitize_dark_purple_ink(cell: Image.Image) -> None:
    """Normalize near-black purple quantisation to neutral selective ink."""
    pixels = cell.load()
    for y in range(REF_CELL):
        for x in range(REF_CELL):
            p = pixels[x, y]
            if p == MAGENTA:
                continue
            h, s, v = hsv(p)
            if 280.0 / 360.0 <= h <= 345.0 / 360.0 and s > 0.5 and v <= 0.25:
                pixels[x, y] = INK


def warm_light(pixel: tuple[int, int, int]) -> bool:
    """Match amber lamp/glow pixels without touching teal or neutral metal."""
    r, g, b = pixel
    return r > 90 and g > 25 and b < 150 and r > g * 1.05


def repaint_bottom_diamond(cell: Image.Image, clean_pose: Image.Image) -> None:
    """Turn the base pose's bottom diamond from amber to the friendly red.

    The mask is learned from the clean state-0 pose at the same cell. This
    keeps the state-1/state-2 damage pixels locked while selecting only the
    lamp's existing amber footprint, rather than repainting orange damage or
    the engine nozzles.
    """
    ref = clean_pose.load()
    current = cell.load()
    candidates: list[tuple[int, int]] = []
    for y in range(330, 430):
        for x in range(160, 350):
            r, g, b = ref[x, y]
            if r >= 125 and g >= 115 and b < 100:
                candidates.append((x, y))

    if not candidates:
        return

    # The bright horizontal lamp bar has a dense row; off poses only have
    # scattered amber seam pixels and therefore do not pass this gate.
    row_counts: dict[int, int] = {}
    for _, y in candidates:
        row_counts[y] = row_counts.get(y, 0) + 1
    peak_y, peak_count = max(row_counts.items(), key=lambda item: item[1])
    peak_xs = [x for x, y in candidates if y == peak_y]
    if peak_count < 8:
        return
    anchor_x = sum(peak_xs) / len(peak_xs)

    for y in range(max(0, peak_y - 34), min(REF_CELL, peak_y + 21)):
        for x in range(max(0, int(anchor_x - 46)), min(REF_CELL, int(anchor_x + 47))):
            if not warm_light(ref[x, y]):
                continue
            p = current[x, y]
            if not warm_light(p):
                continue
            value = luminance(p)
            current[x, y] = RED_HOT if value >= 135 else RED_LIGHT


def build(state: int) -> None:
    reference = Image.open(ROOT / f"skinref_state{state}.png").convert("RGB")
    clean_reference = Image.open(ROOT / "skinref_state0.png").convert("RGB")
    if reference.size != (1536, 1536):
        raise ValueError(f"reference state{state}: expected 1536x1536, got {reference.size}")

    sheet = Image.new("RGB", (OUT_CELL * 3, OUT_CELL * 3), MAGENTA)
    for row in range(3):
        for col in range(3):
            cell = reference.crop((
                col * REF_CELL,
                row * REF_CELL,
                (col + 1) * REF_CELL,
                (row + 1) * REF_CELL,
            ))
            painted = paint_bone_cell(cell, row, col)
            if not (row == 2 and col == 2):
                clean_pose = clean_reference.crop((
                    col * REF_CELL,
                    row * REF_CELL,
                    (col + 1) * REF_CELL,
                    (row + 1) * REF_CELL,
                ))
                repaint_bottom_diamond(painted, clean_pose)
            sanitize_dark_purple_ink(painted)
            painted = painted.resize((OUT_CELL, OUT_CELL), Image.Resampling.NEAREST)
            sheet.paste(painted, (col * OUT_CELL, row * OUT_CELL))

    sheet.save(ROOT / f"skin_Bone_state{state}.png", format="PNG", optimize=False)


if __name__ == "__main__":
    for state in range(3):
        build(state)
        print(f"wrote skin_Bone_state{state}.png")
