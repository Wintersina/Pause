"""Build the Eclipse split-livery from the approved SolarFang flipbook.

The generated repaint candidates in this directory were used as the visual
material reference for the eclipse ramp.  This final pass locks every pixel of
the approved sheets' geometry, then paints only the left-hand armour with a
discrete charcoal-violet ramp.  The export is enlarged with nearest-neighbour
to preserve the game's hard pixel edges.
"""

from colorsys import rgb_to_hsv
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
REFERENCE = [ROOT / f"skinref_state{state}.png" for state in range(3)]
OUTPUT = [ROOT / f"skin_Eclipse_state{state}.png" for state in range(3)]

# The generated candidates establish the intended hand-painted ramp.  These
# exact hard bands keep it compatible with the neon pixel-art guide.
ECLIPSE_SHADOW = (26, 26, 40)       # #1A1A28
ECLIPSE_DEEP = (34, 35, 52)
ECLIPSE_BASE = (44, 45, 64)         # #2C2D40
ECLIPSE_MID = (69, 70, 94)
ECLIPSE_HIGHLIGHT = (90, 92, 120)   # #5A5C78


def hsv(pixel):
    r, g, b = pixel
    return rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)


def is_green(pixel):
    r, g, b = pixel
    return pixel == GREEN or (g > r * 1.05 and g > b * 1.05)


def is_blue_region(pixel):
    r, g, b = pixel
    return not is_green(pixel) and b >= r * 1.08 and b >= g * 0.92 and b > 42


def is_warm_armour(pixel):
    if is_green(pixel) or is_blue_region(pixel) or is_red_light(pixel):
        return False
    hue, saturation, value = hsv(pixel)
    degrees = hue * 360.0
    return 9.0 <= degrees <= 68.0 and saturation > 0.16 and value > 0.08


def is_red_light(pixel):
    r, g, b = pixel
    # The solar armour is orange and often has G=90–150.  The slash lamps
    # and diamonds are the much redder ramp below this narrow green ceiling.
    return r > 150 and g < 85 and b < 85 and r > g * 2.0


def is_gold_like(pixel):
    r, g, b = pixel
    return r > 140 and g > 100 and b < 150 and g / max(r, 1) > 0.62


def is_damage_overlay(pixel, intact_pixel, x, y, spine_x, state_index):
    if state_index == 0 or pixel == intact_pixel or x >= spine_x or y < 220:
        return False
    r, g, b = pixel
    # Retain the unmistakable damage marks: the orange tear/spark cores, the
    # cyan energy fissures, and the compact charcoal/brown scorch islands.
    hot_tear = r > 245 and g > 130 and b < 50 and r > g * 1.15
    cyan_fissure = b > 135 and g > 95 and r < 175
    brown_soot = 28 <= r <= 48 and 18 <= g <= 34 and 17 <= b <= 32 and r >= b
    return hot_tear or cyan_fissure or brown_soot


def reactor_box(cell_pixels):
    """Return a conservative per-pose reactor keep-out and spine centre.

    The reference has a gold/orange ramp shared by the armour and reactor, so
    colour-only detection would swallow the entire hull.  The blue canopy is a
    stable geometric landmark: its bright pixels track the centre spine even
    in the two bank poses.
    """
    canopy = []
    for y in range(55, 285):
        for x in range(145, 367):
            pixel = cell_pixels[y * 512 + x]
            r, g, b = pixel
            if b > 90 and b >= r * 1.12 and b >= g * 0.96:
                canopy.append(x)
    spine_x = sum(canopy) / len(canopy) if canopy else 256.0
    # The reactor ring and its lower collar stay untouched; the generous
    # ellipse also covers the small bank-position translation.
    return (spine_x - 96, 214, spine_x + 96, 412, spine_x, 313)


def in_reactor_or_nozzle(x, y, box):
    min_x, min_y, max_x, max_y, cx, cy = box
    if ((x - cx) / 96.0) ** 2 + ((y - cy) / 99.0) ** 2 <= 1.0:
        return True
    # The nozzle is a separate gold assembly below the reactor.
    return 205 <= x <= 307 and 398 <= y <= 468


def eclipse_paint(pixel):
    r, g, b = pixel
    value = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0
    if value < 0.20:
        return ECLIPSE_SHADOW
    if value < 0.36:
        return ECLIPSE_DEEP
    if value < 0.58:
        return ECLIPSE_BASE
    if value < 0.78:
        return ECLIPSE_MID
    return ECLIPSE_HIGHLIGHT


def bone_flash(pixel):
    """Make the hit-flash cell visibly bone-white while preserving its ink."""
    if is_green(pixel):
        return pixel
    r, g, b = pixel
    if min(r, g, b) >= 165 and max(r, g, b) >= 215:
        value = (r + g + b) / 765.0
        if value > 0.94:
            return (255, 250, 232)
        if value > 0.80:
            return (254, 249, 231)
        return (244, 234, 212)
    return pixel


def repaint(reference, intact_pixels, state_index):
    source = list(reference.getdata())
    output = source[:]
    for cell_row in range(3):
        for cell_col in range(3):
            cell_pixels = [
                source[(cell_row * 512 + y) * 1536 + cell_col * 512 + x]
                for y in range(512)
                for x in range(512)
            ]
            reactor = reactor_box(cell_pixels)
            _, _, _, _, spine_x, _ = reactor
            for y in range(512):
                for x in range(512):
                    index = (cell_row * 512 + y) * 1536 + cell_col * 512 + x
                    pixel = source[index]
                    if cell_row == 2 and cell_col == 2:
                        output[index] = bone_flash(pixel)
                        continue
                    # The approved sheets share the same 3x3 pose order. Any
                    # pixel that differs from intact at this pose is a damage
                    # overlay (scorch, tear, spark, or wreck fragment), so it
                    # stays exactly as drawn instead of being recoloured.
                    damage_overlay = is_damage_overlay(
                        pixel, intact_pixels[index], x, y, spine_x, state_index
                    )
                    source_armour = is_warm_armour(intact_pixels[index])
                    if (
                        x < spine_x
                        and source_armour
                        # Blue canopy/fins remain unchanged.  Only the lower
                        # exposed wreck surface may borrow the eclipse ramp
                        # when damage has revealed a blue-toned source pixel.
                        and (not is_blue_region(pixel) or y > 280)
                        and not in_reactor_or_nozzle(x, y, reactor)
                        and not damage_overlay
                    ):
                        # Solar-orange armour and the exposed dark material
                        # beneath it both inherit the Eclipse ramp from the
                        # approved intact paint values.
                        # Keep the warm split rim beside the spine as the
                        # corona that visually seals the two halves.
                        on_split_rim = abs(x - spine_x) <= 5 and y < 430
                        if not on_split_rim:
                            output[index] = eclipse_paint(intact_pixels[index])
                            continue
    result = Image.new("RGB", reference.size)
    result.putdata(output)
    return result


def main():
    intact = Image.open(REFERENCE[0]).convert("RGB")
    intact_pixels = list(intact.getdata())
    for reference_path, output_path in zip(REFERENCE, OUTPUT):
        reference = Image.open(reference_path).convert("RGB")
        if reference.size != (1536, 1536):
            raise SystemExit(f"{reference_path.name} must be 1536x1536")
        painted = repaint(reference, intact_pixels, REFERENCE.index(reference_path))
        export = painted.resize((3072, 3072), Image.Resampling.NEAREST)
        export.save(output_path, format="PNG", optimize=False)
        print(f"wrote {output_path.name} {export.size[0]}x{export.size[1]}")


if __name__ == "__main__":
    main()
