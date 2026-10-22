"""Build the Night colourway from the approved CrimsonHalo flipbook.

The reference supplies the exact drawing contract (including the nine poses).  This
small raster painter changes only the crimson/raspberry armour ramp and preserves
the source pixels for the canopy, halo, trim, lights, damage, and hit-flash.  The
2x export is deliberately nearest-neighbour so no new edge pixels are invented.
"""

from colorsys import rgb_to_hsv
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)
SCALE = 2
CELL = 512
OUT_CELL = CELL * SCALE


def near_green(rgb):
    r, g, b = rgb
    # The source has a few anti-aliased green fringe pixels.  Flatten those back
    # to the requested chroma key so the finished sheet has a truly flat field.
    return g >= 200 and r <= 40 and b <= 40


def armor_pixel(rgb):
    r, g, b = rgb
    if near_green(rgb) or max(rgb) < 18:
        return False
    h, s, v = rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)

    # Raspberry/magenta armour is the high-hue red family in the approved art.
    # Orange damage and embers are intentionally left alone.
    raspberry = h >= 0.80 and s >= 0.24 and v >= 0.08
    deep_red_armour = h <= 0.12 and s >= 0.20 and v <= 0.95 and r > g * 1.08
    dark_red_shadow = (
        18 <= r < 150 and r >= g and r >= b and (r - g) <= 20
    )
    return raspberry or deep_red_armour or dark_red_shadow


def night_ramp(rgb):
    r, g, b = rgb
    # Preserve the source's hard material bands, but repaint them into a
    # purpose-built indigo/cyan ramp rather than doing a hue rotation.
    value = (0.56 * r + 0.30 * g + 0.14 * b) / 255.0

    if value < 0.16:
        return (26, 31, 69)       # #1A1F45 shadow
    if value < 0.28:
        return (31, 37, 82)
    if value < 0.42:
        return (42, 46, 107)      # #2A2E6B base
    if value < 0.58:
        return (55, 63, 132)
    if value < 0.73:
        return (74, 91, 157)
    if value < 0.87:
        return (110, 242, 238)    # #6EF2EE rim light
    return (224, 255, 255)        # sparse pale blue-white hot pixel


def keep_warm_effect(rgb, x, y, state):
    # Keep the reference's orange emissive details: engine nozzles and wingtip
    # diamonds in every state, plus the explicit damaged-wing effect zones.
    r, g, b = rgb
    h, s, v = rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
    orange = h <= 0.12 and s >= 0.25 and g > 35 and g > b * 1.15
    if not orange:
        return False
    if y >= 420 and 205 <= x <= 307:
        return True
    if (x <= 90 or x >= 422) and 260 <= y <= 430:
        return True
    if state == 1:
        return 110 <= y <= 430 and x <= 145
    if state == 2:
        return 110 <= y <= 430 and (x <= 125 or x >= 387)
    return False


def paint_cell(cell, state, hit_flash=False):
    pixels = cell.load()
    for y in range(CELL):
        for x in range(CELL):
            rgb = pixels[x, y]
            if near_green(rgb):
                pixels[x, y] = GREEN
            elif hit_flash:
                # The hit-flash remains bone-white, but gets the Night skin's
                # cooler white ramp so the cell is still visibly repainted.
                r, g, b = rgb
                if min(rgb) > 24:
                    pixels[x, y] = (
                        max(0, min(255, int(r * 0.93 + 8))),
                        max(0, min(255, int(g * 0.95 + 7))),
                        max(0, min(255, int(b * 0.98 + 6))),
                    )
            elif armor_pixel(rgb) and not keep_warm_effect(rgb, x, y, state):
                pixels[x, y] = night_ramp(rgb)
    return cell


def build(state):
    source = Image.open(ROOT / f"skinref_state{state}.png").convert("RGB")
    if source.size != (CELL * 3, CELL * 3):
        raise AssertionError(f"unexpected reference size: {source.size}")

    output = Image.new("RGB", (OUT_CELL * 3, OUT_CELL * 3), GREEN)
    for row in range(3):
        for col in range(3):
            box = (col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL)
            # The bottom-right pose is the approved bone-white hit flash.
            cell = paint_cell(source.crop(box), state, hit_flash=(row == 2 and col == 2))
            scaled = cell.resize((OUT_CELL, OUT_CELL), Image.Resampling.NEAREST)
            output.paste(scaled, (col * OUT_CELL, row * OUT_CELL))

    output.save(ROOT / f"skin_Night_state{state}.png")


def main():
    for state in range(3):
        build(state)


if __name__ == "__main__":
    main()
