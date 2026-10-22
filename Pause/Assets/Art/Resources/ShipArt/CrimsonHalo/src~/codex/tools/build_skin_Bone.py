"""Bake the Bone colourway from the generated paint guides.

The guides are AI-painted Bone redraws.  The approved sheets remain the
pixel-faithful drawing source: only material colours are sampled/quantised
from the guides, while the reference supplies every silhouette, seam, crack,
glint, canopy pixel, and cell placement.  The final sheet is upscaled with
nearest-neighbour exactly 2x.
"""

from colorsys import rgb_to_hsv
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)
CELL = 512
OUT_CELL = 1024
SHEET = CELL * 3

# Warm ceramic ramp, from shadow to hot highlight.
BONE_RAMP = (
    (137, 127, 110),  # warm deep ceramic shadow
    (185, 174, 152),  # #B9AE98 requested shadow
    (216, 204, 179),  # warm grey plate light
    (244, 234, 212),  # #F4EAD4 requested base
    (255, 248, 231),  # near-white highlight
)

# Halo ring ramp, from shadow to warm red edge light.
CRIMSON_RAMP = (
    (110, 17, 54),   # #6E1136 requested shadow
    (148, 28, 75),
    (200, 40, 94),   # #C8285E requested base
    (237, 63, 67),   # warm red highlight
)

SIGNAL_BASE = (216, 35, 44)   # #D8232C
SIGNAL_CORE = (255, 91, 69)   # #FF5B45


def near_green(rgb):
    return rgb == GREEN


def is_blue(rgb):
    r, g, b = rgb
    return b > r * 1.15 and b > g * 1.02 and b > 70 and g > 35


def hsv(rgb):
    r, g, b = rgb
    return rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)


def is_warm_effect(rgb):
    """Keep the approved orange tears, sparks, embers, and nozzle light."""
    r, g, b = rgb
    h, s, _ = hsv(rgb)
    # Hue 0.00 is the player armour red.  Damage FX are the orange slice of
    # the wheel, so do not let the armour's bright red ramp escape this skin.
    return 0.035 <= h <= 0.12 and s >= 0.25 and g > 35 and g > b * 1.15


def is_armour(rgb):
    """Identify the approved raspberry/red armour, not orange damage FX."""
    if near_green(rgb) or is_blue(rgb) or is_warm_effect(rgb):
        return False
    r, g, b = rgb
    h, s, v = hsv(rgb)
    raspberry = h >= 0.80 and s >= 0.24 and v >= 0.08
    red_armour = h <= 0.035 and s >= 0.45 and r > g * 1.35 and r > b * 1.25
    return raspberry or red_armour


def is_neutral(rgb):
    """Cream/white reference pixels used by the halo and hit flash."""
    if near_green(rgb) or is_blue(rgb):
        return False
    r, g, b = rgb
    return min(rgb) > 90 and max(rgb) - min(rgb) < 82


def is_gold(rgb):
    """Gold/red signal pixels such as the centre mark and wingtip diamonds."""
    if near_green(rgb) or is_blue(rgb):
        return False
    h, s, v = hsv(rgb)
    return 0.045 <= h <= 0.18 and s >= 0.28 and v >= 0.35


def guide_is_crimson(rgb):
    """Semantic cue from the AI repaint: its crimson halo and signal nodes."""
    r, g, b = rgb
    h, s, v = hsv(rgb)
    return r > 95 and r > g * 1.22 and r > b * 1.08 and s > 0.32 and v > 0.25


def is_halo_zone(x, y):
    """Loose upper-cell envelope for the plated ring in banked poses."""
    lx, ly = x % CELL, y % CELL
    return ly < 292 and abs(lx - 256) < 190


def luminance(rgb):
    r, g, b = rgb
    return (0.56 * r + 0.30 * g + 0.14 * b) / 255.0


def ramp_from_guide(guide_rgb, ramp):
    value = luminance(guide_rgb)
    if value < 0.22:
        return ramp[0]
    if value < 0.43:
        return ramp[1]
    if value < 0.66:
        return ramp[2]
    if value < 0.84:
        return ramp[3]
    return ramp[-1]


def signal_from_pixels(ref_rgb, guide_rgb):
    """Keep the red signal flat and make the brightest reference pixels hot."""
    value = max(luminance(ref_rgb), luminance(guide_rgb))
    return SIGNAL_CORE if value >= 0.72 else SIGNAL_BASE


def build(state, guide_name):
    reference = Image.open(ROOT / f"skinref_state{state}.png").convert("RGB")
    guide = Image.open(ROOT / "tools" / guide_name).convert("RGB")
    if reference.size != (SHEET, SHEET):
        raise AssertionError(f"reference state{state}: expected {SHEET}x{SHEET}, got {reference.size}")

    # Guides are only semantic paint references.  The output remains on the
    # reference grid, with no generated geometry copied into the final sheet.
    guide = guide.resize((SHEET, SHEET), Image.Resampling.NEAREST)
    ref_px = reference.load()
    guide_px = guide.load()
    painted = Image.new("RGB", (SHEET, SHEET), GREEN)
    out_px = painted.load()

    for y in range(SHEET):
        for x in range(SHEET):
            ref_rgb = ref_px[x, y]
            if near_green(ref_rgb):
                continue

            guide_rgb = guide_px[x, y]
            row, col = y // CELL, x // CELL
            hit_flash = row == 2 and col == 2

            if hit_flash and not is_blue(ref_rgb) and min(ref_rgb) > 90:
                # The flash remains ceramic white, but is repainted into the
                # Bone warm-white ramp so it is visibly part of this skin.
                out_px[x, y] = ramp_from_guide(guide_rgb, BONE_RAMP)
            elif is_armour(ref_rgb):
                out_px[x, y] = ramp_from_guide(guide_rgb, BONE_RAMP)
            elif is_neutral(ref_rgb) and (guide_is_crimson(guide_rgb) or is_halo_zone(x, y)):
                # The reference halo was bone-white; the generated guide marks
                # its corresponding plated ring as the new crimson material.
                out_px[x, y] = ramp_from_guide(guide_rgb, CRIMSON_RAMP)
            elif is_gold(ref_rgb) and guide_is_crimson(guide_rgb):
                out_px[x, y] = signal_from_pixels(ref_rgb, guide_rgb)
            else:
                # Canopy glass, gunmetal, orange damage, embers, soot, glints,
                # and nozzle glow stay pixel-identical to the approved sheet.
                out_px[x, y] = ref_rgb

    output = Image.new("RGB", (OUT_CELL * 3, OUT_CELL * 3), GREEN)
    for row in range(3):
        for col in range(3):
            box = (col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL)
            out_cell = painted.crop(box).resize((OUT_CELL, OUT_CELL), Image.Resampling.NEAREST)
            output.paste(out_cell, (col * OUT_CELL, row * OUT_CELL))
    output.save(ROOT / f"skin_Bone_state{state}.png")


def main():
    guides = {
        0: "candidate_Bone_state0_b.png",
        1: "candidate_Bone_state1_a.png",
        2: "candidate_Bone_state2_a.png",
    }
    for state, guide_name in guides.items():
        build(state, guide_name)


if __name__ == "__main__":
    main()
