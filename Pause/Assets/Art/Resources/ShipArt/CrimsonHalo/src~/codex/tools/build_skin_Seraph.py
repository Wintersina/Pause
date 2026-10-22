"""Build the Seraph colourway while locking the approved flipbook geometry.

The source sheets are the drawing authority.  This pass keeps their exact green
background, masks, pose anchors, panel pixels, blue glass, and damage pixels,
then repaints material classes with hard Seraph ramps and adds the livery only
inside existing armour pixels.  The final 2x export is nearest-neighbour.
"""

from pathlib import Path
from colorsys import rgb_to_hsv

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)
CELL = 512
OUT_CELL = 1024

INK = (5, 6, 12)
OXBLOOD = ((38, 10, 20), (53, 16, 31), (74, 20, 36), (97, 29, 49), (122, 42, 60))
ROSE = ((168, 106, 76), (226, 162, 124), (246, 210, 176))
WARM_WHITE = (244, 234, 212)


def is_blue(pixel):
    r, g, b = pixel
    return b > r * 1.15 and b > g * 1.02 and b > 70 and g > 35


def is_orange_fx(pixel):
    r, g, b = pixel
    if not (r > 120 and g > 42 and b < 130 and r > g * 1.15):
        return False
    hue = rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)[0] * 360.0
    return 18.0 <= hue <= 55.0


def is_red_armour(pixel):
    r, g, b = pixel
    # The source's raspberry/magenta armour ramp.  Orange sparks are excluded
    # first so the damage effects remain their approved warm colour.
    if is_orange_fx(pixel):
        return False
    return r > 26 and r > g * 1.30 and r > b * 1.18 and g < 155


def is_magenta_residue(pixel):
    r, g, b = pixel
    hue, saturation, _ = rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
    degrees = hue * 360.0
    return saturation > 0.35 and 300.0 <= degrees <= 345.0


def is_bone(pixel):
    r, g, b = pixel
    return r > 150 and g > 135 and b > 105 and max(pixel) - min(pixel) < 105


def luminance(pixel):
    r, g, b = pixel
    return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0


def ramp_colour(pixel):
    value = luminance(pixel)
    if value < 0.20:
        return OXBLOOD[0]
    if value < 0.36:
        return OXBLOOD[1]
    if value < 0.54:
        return OXBLOOD[2]
    if value < 0.74:
        return OXBLOOD[3]
    return OXBLOOD[4]


def halo_roi(x, y):
    # The open ring is the cream material around the canopy, not the nose or
    # lower white engine pixels.  This broad ROI also covers banked poses.
    return 135 <= x <= 377 and 48 <= y <= 265


def make_livery_mask(source):
    """Return hard-edged mask pixels for the central and wing chevrons."""
    mask = Image.new("1", (CELL, CELL), 0)
    draw = ImageDraw.Draw(mask)

    # Three stacked upward-pointing chevrons on the centre fuselage.
    for y in (276, 309, 342):
        draw.line([(226, y + 8), (256, y - 8), (286, y + 8)], fill=1, width=8, joint="curve")

    # Matching leading-edge stripes following the wing plates.
    draw.line([(224, 255), (190, 278), (153, 309), (113, 342), (83, 361)], fill=1, width=7, joint="curve")
    draw.line([(288, 255), (322, 278), (359, 309), (399, 342), (429, 361)], fill=1, width=7, joint="curve")

    source_pixels = source.load()
    mask_pixels = mask.load()
    for y in range(CELL):
        for x in range(CELL):
            if mask_pixels[x, y] and not is_red_armour(source_pixels[x, y]):
                mask_pixels[x, y] = 0
    return mask


def paint_cell(source, row, col):
    src = source.load()
    out = source.copy()
    dst = out.load()
    flash = row == 2 and col == 2
    livery = make_livery_mask(source) if not flash else None
    livery_pixels = livery.load() if livery else None

    for y in range(CELL):
        for x in range(CELL):
            p = src[x, y]
            if p == GREEN:
                continue

            # The hit flash remains bone-white, but gets a warm-white bone
            # ramp so this cell is still an intentional colourway export.
            if flash:
                if is_bone(p):
                    value = luminance(p)
                    dst[x, y] = (244, 234, 212) if value > 0.78 else (222, 211, 192)
                else:
                    dst[x, y] = p
                continue

            # Exact canopy/blue-region preservation is a hard invariant.
            if is_blue(p):
                dst[x, y] = p
                continue

            # Orange tears, sparks and embers stay as approved damage FX.
            if is_orange_fx(p):
                dst[x, y] = p
                continue

            # Remove any source magenta shadow/highlight that the red-armour
            # test cannot catch because its blue channel is unusually high.
            if is_magenta_residue(p):
                dst[x, y] = ramp_colour(p)
                continue

            # Repaint the ring's bone material into rose-gold with warm-white
            # facets; seams and dark sockets remain untouched below.
            if halo_roi(x, y) and is_bone(p):
                # The reference halo is predominantly near-white, so use a
                # sparse spatial highlight pattern instead of letting every
                # bright source pixel remain white.  This preserves the
                # existing facets while making the new metal unmistakably
                # rose-gold.
                facet = (x * 13 + y * 7) % 17
                if facet in (0, 1, 2):
                    dst[x, y] = WARM_WHITE
                elif facet in (3, 4):
                    dst[x, y] = ROSE[2]
                else:
                    dst[x, y] = ROSE[1]
                continue

            # All source red/raspberry armour, including red anti-aliased ramp
            # pixels, becomes the oxblood-black wine metal ramp.
            if is_red_armour(p):
                dst[x, y] = ramp_colour(p)
                continue

            # Add the livery only on existing armour pixels, preserving dark
            # panel seams, blue trim, and the exact silhouette.
            if livery_pixels[x, y] and is_red_armour(p):
                dst[x, y] = ROSE[1]

    # Paint the rose-gold livery as a second hard-edged ramp pass on top of the
    # oxblood result, but only where the original was armour.
    if livery_pixels:
        original = source.load()
        final = out.load()
        for y in range(CELL):
            for x in range(CELL):
                if not livery_pixels[x, y] or not is_red_armour(original[x, y]):
                    continue
                edge = ((x + y) % 7 == 0)
                final[x, y] = ROSE[2] if edge else ROSE[1]

    return out


def build_state(state):
    reference = Image.open(ROOT / f"skinref_state{state}.png").convert("RGB")
    sheet = Image.new("RGB", (OUT_CELL * 3, OUT_CELL * 3), GREEN)
    for row in range(3):
        for col in range(3):
            source = reference.crop((col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL))
            painted = paint_cell(source, row, col)
            up = painted.resize((OUT_CELL, OUT_CELL), Image.Resampling.NEAREST)
            sheet.paste(up, (col * OUT_CELL, row * OUT_CELL))
    sheet.save(ROOT / f"skin_Seraph_state{state}.png", format="PNG", optimize=False)


def main():
    for state in range(3):
        build_state(state)
        print(f"wrote skin_Seraph_state{state}.png")


if __name__ == "__main__":
    main()
