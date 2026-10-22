"""Build the Neon SolarFang colourway from the approved flipbook geometry.

The image-generation candidates were used as the repaint reference for the
material ramp. This final pass keeps the approved pixel geometry and paints
the orange material into a discrete teal ramp before the required nearest-
neighbour export. Non-armour pixels are copied verbatim.
"""

from colorsys import rgb_to_hsv
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
REFERENCE = [ROOT / f"skinref_state{state}.png" for state in range(3)]
OUTPUT = [ROOT / f"skin_Neon_state{state}.png" for state in range(3)]

# Neon teal material ramp, from the requested brief. The two middle tones
# keep the generated repaint's hard-banded pixel shading without gradients.
TEAL_SHADOW = (15, 94, 106)       # #0F5E6A
TEAL_DEEP = (20, 132, 141)
TEAL_BASE = (31, 181, 185)        # #1FB5B9
TEAL_LIGHT = (110, 242, 238)      # #6EF2EE
TEAL_HOT = (245, 253, 253)        # white-cyan hot edge


def hsv(pixel):
    r, g, b = pixel
    return rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)


def is_green(pixel):
    r, g, b = pixel
    return pixel == GREEN or (g > r * 1.05 and g > b * 1.05)


def is_blue_region(pixel):
    r, g, b = pixel
    return not is_green(pixel) and b >= r * 1.08 and b >= g * 0.92 and b > 42


def is_warm_material(pixel):
    r, g, b = pixel
    hue, saturation, value = hsv(pixel)
    degrees = hue * 360.0
    return 8.0 <= degrees <= 50.0 and saturation > 0.15 and value > 0.12


def is_gold_like(pixel):
    r, g, b = pixel
    return r > 140 and g > 100 and b < 140 and g / max(r, 1) > 0.55


def is_reactor_area(local_x, local_y):
    reactor = ((local_x - 256.0) / 92.0) ** 2 + ((local_y - 305.0) / 92.0) ** 2
    return reactor < 1.0 or (220 < local_x < 292 and 400 < local_y < 466)


def is_damage_glow(pixel):
    r, g, b = pixel
    hue, saturation, value = hsv(pixel)
    return r > 240 and g > 140 and b < 70 and 8.0 <= hue * 360.0 <= 55.0


def is_wreck_glow(pixel, local_x, local_y):
    r, g, b = pixel
    central_damage = 175 <= local_x <= 337 and 320 <= local_y <= 430
    return central_damage and r > 230 and g > 80 and b < 30


def remove_magenta_fringe(pixel):
    r, g, b = pixel
    hue, saturation, value = hsv(pixel)
    if 300.0 <= hue * 360.0 <= 345.0 and saturation > 0.5 and value > 0.08:
        return (r, g, min(b, g))
    return pixel


def is_armour_source(pixel, local_x, local_y):
    """Identify warm painted armour, excluding the gold reactor/nozzle.

    The source mask is taken from the intact sheet at the same pose. This is
    what lets damaged/wrecked sheets inherit the exact same painted hull
    coverage while leaving newly exposed damage pixels structurally intact.
    """
    r, g, b = pixel
    hue, saturation, value = hsv(pixel)
    if not (8.0 <= hue * 360.0 <= 65.0 and saturation > 0.15 and value > 0.12):
        return False

    # The reactor ring and lower nozzle are warm gold and must remain warm.
    # These local keep-outs cover the small bob/bank shifts while avoiding the
    # larger orange armour planes around them.
    reactor = ((local_x - 256.0) / 72.0) ** 2 + ((local_y - 305.0) / 72.0) ** 2
    if reactor < 1.0:
        return False
    if 220 < local_x < 292 and 400 < local_y < 466:
        return False
    return True


def paint_teal(pixel):
    """Quantise the orange ramp ratio into a crisp neon-teal ramp."""
    r, g, b = pixel
    orange_light = g / max(r, 1)
    if orange_light < 0.22:
        return TEAL_SHADOW
    if orange_light < 0.48:
        return TEAL_DEEP
    if orange_light < 0.70:
        return TEAL_BASE
    if orange_light < 0.86:
        return TEAL_LIGHT
    return TEAL_HOT


def paint_hit_flash(pixel):
    """Keep the hit flash bone-white while giving the colourway a real delta."""
    value = max(pixel)
    if value >= 245:
        return (255, 246, 222)
    if value >= 210:
        return (250, 239, 216)
    return pixel


def repaint(reference_state, intact_reference, state_index):
    width, height = reference_state.size
    source_pixels = list(intact_reference.getdata())
    target_pixels = list(reference_state.getdata())
    painted = []
    for index, (source, target) in enumerate(zip(source_pixels, target_pixels)):
        x = index % width
        y = index // width
        local_x = x % 512
        local_y = y % 512
        hit_flash = local_x >= 0 and local_y >= 0 and (x // 512, y // 512) == (2, 2)
        preserve_gold = is_gold_like(target) and is_reactor_area(local_x, local_y)
        target_is_paintable = is_warm_material(target) and not is_blue_region(target) and not preserve_gold
        if hit_flash and not is_green(target):
            painted.append(paint_hit_flash(target))
        elif is_damage_glow(target) or (state_index == 2 and is_wreck_glow(target, local_x, local_y)):
            painted.append(target)
        elif (is_armour_source(source, local_x, local_y) or target_is_paintable) and not is_green(target) and not is_blue_region(target) and not preserve_gold:
            painted.append(paint_teal(source if is_armour_source(source, local_x, local_y) else target))
        else:
            painted.append(target if is_blue_region(target) else remove_magenta_fringe(target))
    result = Image.new("RGB", reference_state.size)
    result.putdata(painted)
    return result


def main():
    intact = Image.open(REFERENCE[0]).convert("RGB")
    if intact.size != (1536, 1536):
        raise SystemExit("skinref_state0.png must be 1536x1536")

    for reference_path, output_path in zip(REFERENCE, OUTPUT):
        reference = Image.open(reference_path).convert("RGB")
        if reference.size != (1536, 1536):
            raise SystemExit(f"{reference_path.name} must be 1536x1536")
        repaint_1536 = repaint(reference, intact, REFERENCE.index(reference_path))
        repaint_3072 = repaint_1536.resize((3072, 3072), Image.Resampling.NEAREST)
        repaint_3072.save(output_path, format="PNG", optimize=False)
        print(f"wrote {output_path.name} {repaint_3072.size[0]}x{repaint_3072.size[1]}")


if __name__ == "__main__":
    main()
