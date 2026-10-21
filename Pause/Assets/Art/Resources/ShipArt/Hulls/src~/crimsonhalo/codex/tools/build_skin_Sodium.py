"""Bake the Sodium colourway from generated paint guides and locked references.

The generated candidates supply the new orange material treatment.  The approved
reference supplies the exact pose pixels, so the final export cannot drift in
silhouette, canopy, damage, or cell placement.  The guide is sampled only for
the armour ramp; all other reference pixels are carried through unchanged.
"""

from colorsys import rgb_to_hsv
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)
CELL = 512
OUT_CELL = 1024
SHEET = CELL * 3

# Sodium-lamp armour ramp: shadow, base, highlight, and sparse hot edge.
SODIUM = (
    (169, 72, 26),   # #A9481A
    (242, 134, 43),  # #F2862B
    (255, 180, 60),  # #FFB43C
    (255, 241, 180), # pale yellow-white hot edge
)


def near_green(rgb):
    r, g, b = rgb
    return g >= 200 and r <= 40 and b <= 40


def is_blue(rgb):
    r, g, b = rgb
    return b > r * 1.15 and b > g * 1.02 and b > 70 and g > 35


def is_armour(rgb):
    if near_green(rgb) or max(rgb) < 18:
        return False
    r, g, b = rgb
    h, s, v = rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)

    # The approved armour is raspberry/magenta.  The second condition catches
    # the small pure-red edge pixels in the wreck sheet without catching the
    # orange damage effects (which are retained below).
    raspberry = h >= 0.80 and s >= 0.24 and v >= 0.08
    red_armour = h <= 0.025 and s >= 0.45 and r > g * 1.35 and r > b * 1.25
    return raspberry or red_armour


def is_warm_effect(rgb):
    """Keep existing orange damage, sparks, embers, and nozzle light intact."""
    r, g, b = rgb
    h, s, v = rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
    return h <= 0.12 and s >= 0.25 and g > 35 and g > b * 1.15


def ramp_from_guide(rgb):
    r, g, b = rgb
    value = (0.56 * r + 0.30 * g + 0.14 * b) / 255.0
    if value < 0.25:
        return SODIUM[0]
    if value < 0.56:
        return SODIUM[1]
    if value < 0.80:
        return SODIUM[2]
    return SODIUM[3]


def warm_bone(rgb, guide_rgb):
    """Tint bright hit-flash material while keeping it bone-white."""
    r, g, b = rgb
    value = (0.56 * guide_rgb[0] + 0.30 * guide_rgb[1] + 0.14 * guide_rgb[2]) / 255.0
    if value >= 0.86:
        return (255, 252, 239)
    if value >= 0.70:
        return (250, 242, 222)
    if value >= 0.52:
        return (238, 228, 207)
    return rgb


def build(state):
    reference = Image.open(ROOT / f"skinref_state{state}.png").convert("RGB")
    guide = Image.open(ROOT / "tools" / f"candidate_Sodium_state{state}.png").convert("RGB")
    if reference.size != (SHEET, SHEET):
        raise AssertionError(f"reference state{state}: {reference.size}")

    # Candidate images are generated at a different square size.  Resizing the
    # colour guide to the reference canvas is only for material sampling; the
    # final drawing remains on the exact reference pixel grid.
    guide = guide.resize((SHEET, SHEET), Image.Resampling.BICUBIC)
    ref_px = reference.load()
    guide_px = guide.load()

    painted = Image.new("RGB", (SHEET, SHEET), GREEN)
    out_px = painted.load()
    for y in range(SHEET):
        for x in range(SHEET):
            rgb = ref_px[x, y]
            if near_green(rgb):
                out_px[x, y] = GREEN
                continue

            guide_rgb = guide_px[x, y]
            row, col = y // CELL, x // CELL
            hit_flash = row == 2 and col == 2

            if hit_flash and not is_blue(rgb) and min(rgb) > 105:
                out_px[x, y] = warm_bone(rgb, guide_rgb)
            elif is_armour(rgb) and not is_warm_effect(rgb):
                out_px[x, y] = ramp_from_guide(guide_rgb)
            else:
                out_px[x, y] = rgb

    output = Image.new("RGB", (OUT_CELL * 3, OUT_CELL * 3), GREEN)
    for row in range(3):
        for col in range(3):
            box = (col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL)
            cell = painted.crop(box).resize((OUT_CELL, OUT_CELL), Image.Resampling.NEAREST)
            output.paste(cell, (col * OUT_CELL, row * OUT_CELL))

    output.save(ROOT / f"skin_Sodium_state{state}.png")


def main():
    for state in range(3):
        build(state)


if __name__ == "__main__":
    main()
