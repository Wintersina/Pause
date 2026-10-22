from __future__ import annotations

import colorsys
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
REF_SIZE = 1536
CELL = 512
OUT_CELL = 1024
OUT_SIZE = 3072

# Discrete Bone ramps selected from the generated Bone repaint candidates.
# They are intentionally material ramps, not a hue rotation of the source.
ARMOUR_RAMP = (
    (78, 72, 65),       # deep ceramic shadow
    (128, 119, 103),
    (185, 174, 152),    # required warm-grey shadow
    (216, 205, 184),
    (244, 234, 212),    # required Bone base
    (255, 250, 232),    # near-white highlight
)
SCUTE_RAMP = (
    (137, 122, 99),
    (184, 163, 128),
    (218, 195, 155),
    (234, 217, 180),    # required warm cream
    (247, 232, 198),
    (255, 244, 215),
)


def hsv(rgb: tuple[int, int, int]) -> tuple[float, float, float]:
    r, g, b = rgb
    return colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)


def is_green(rgb: tuple[int, int, int]) -> bool:
    return rgb == GREEN


def is_canopy(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    return g >= 95 and b >= 95 and b >= r + 28 and g >= r + 18


def is_emitter(rgb: tuple[int, int, int]) -> bool:
    """Protect cyan, red, amber, and orange damage FX from the armour pass."""
    if is_canopy(rgb):
        return True
    r, g, b = rgb
    hue, sat, value = hsv(rgb)
    deg = hue * 360.0
    # Keep the broad gold/brown armour range available to the Bone ramp.  Only
    # the saturated, bright orange/red pixels are damage or emissive FX.
    if sat >= 0.56 and value >= 0.42 and (deg <= 12.0 or deg >= 345.0):
        return True
    if 15.0 <= deg <= 28.0 and sat > 0.78 and value > 0.55:
        return True
    return False


def is_intact_light(rgb: tuple[int, int, int]) -> bool:
    """Lights that must remain bright on the otherwise undamaged sheet."""
    if is_canopy(rgb):
        return True
    hue, sat, value = hsv(rgb)
    deg = hue * 360.0
    return sat >= 0.56 and value >= 0.42 and (deg <= 12.0 or deg >= 345.0)


def is_centre_lamp(x: int, y: int, rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    hue, sat, value = hsv(rgb)
    deg = hue * 360.0
    # Tight diamond footprint; the surrounding warm plate is armour and must
    # not be left in the source gold colour.
    return (248 <= x <= 268 and 274 <= y <= 294 and 27.0 <= deg <= 48.0
            and sat >= 0.62 and value >= 0.45 and r >= 120 and g >= 55 and b < 90)


def is_magenta(rgb: tuple[int, int, int]) -> bool:
    hue, sat, _ = hsv(rgb)
    return sat > 0.50 and 295.0 <= hue * 360.0 <= 345.0


def clean_magenta(image: Image.Image) -> Image.Image:
    """Remove dark purple fringe while retaining the exact non-green mask."""
    source = image.copy().convert("RGB")
    output = source.copy()
    for y in range(CELL):
        for x in range(CELL):
            if not is_magenta(source.getpixel((x, y))):
                continue
            replacement = None
            for radius in range(1, 5):
                for ox, oy in (
                    (-radius, 0), (radius, 0), (0, -radius), (0, radius),
                    (-radius, -radius), (radius, -radius),
                    (-radius, radius), (radius, radius),
                ):
                    nx, ny = x + ox, y + oy
                    if 0 <= nx < CELL and 0 <= ny < CELL:
                        candidate = source.getpixel((nx, ny))
                        if candidate != GREEN and not is_magenta(candidate):
                            replacement = candidate
                            break
                if replacement is not None:
                    break
            output.putpixel((x, y), replacement or (5, 6, 12))
    return output


def is_shell_armour(rgb: tuple[int, int, int]) -> bool:
    """Select the tan/gold shell without selecting FX, ink, or gunmetal."""
    if is_green(rgb) or is_emitter(rgb):
        return False
    r, g, b = rgb
    hue, sat, value = hsv(rgb)
    deg = hue * 360.0
    # Brown/gold shell: warm hue, enough red/green, and not a neutral edge.
    return (14.0 <= deg <= 72.0 and r >= g * 1.08 and g >= b * 1.35
            and sat >= 0.20 and value >= 0.18)


def is_scute(rgb: tuple[int, int, int]) -> bool:
    """Select pale sand scutes while leaving lights and glass untouched."""
    if is_green(rgb) or is_emitter(rgb):
        return False
    r, g, b = rgb
    hue, sat, value = hsv(rgb)
    deg = hue * 360.0
    return (r >= 132 and g >= 116 and b >= 62 and r >= g - 4
            and g >= b + 7 and sat <= 0.56 and 12.0 <= deg <= 78.0
            and value >= 0.48)


def is_hit_flash_material(rgb: tuple[int, int, int]) -> bool:
    """The approved hit frame is already neutral; warm it into Bone ceramic."""
    if is_green(rgb) or is_canopy(rgb) or is_emitter(rgb):
        return False
    r, g, b = rgb
    # Neutral/pale ceramic only; do not repaint ink or the dark flippers.
    return min(r, g, b) >= 54 and max(rgb) - min(rgb) <= 48


def is_plate_highlight(source: Image.Image, x: int, y: int) -> bool:
    """Catch near-white ceramic highlights sitting inside a warm plate."""
    rgb = source.getpixel((x, y))
    if is_green(rgb) or is_canopy(rgb) or is_emitter(rgb):
        return False
    r, g, b = rgb
    if min(rgb) < 150 or max(rgb) - min(rgb) > 42:
        return False
    for oy in range(-2, 3):
        for ox in range(-2, 3):
            if not ox and not oy:
                continue
            nx, ny = x + ox, y + oy
            if 0 <= nx < CELL and 0 <= ny < CELL:
                neighbor = source.getpixel((nx, ny))
                if is_shell_armour(neighbor) or is_scute(neighbor):
                    return True
    return False


def ramp_colour(ramp: tuple[tuple[int, int, int], ...], rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    value = max(rgb) / 255.0
    # Hard bands retain the hand-placed pixel-art texture of each plate.
    index = min(len(ramp) - 1, int(value * len(ramp)))
    return ramp[index]


def changed_ramp_colour(
    ramp: tuple[tuple[int, int, int], ...], rgb: tuple[int, int, int], x: int, y: int
) -> tuple[int, int, int]:
    """Keep an already-neutral source pixel from hiding the new paint pass."""
    value = max(rgb) / 255.0
    index = min(len(ramp) - 1, int(value * len(ramp)))
    target = ramp[index]
    if target != rgb:
        return target
    for offset in (1, -1, 2, -2):
        candidate = ramp[max(0, min(len(ramp) - 1, index + offset))]
        if candidate != rgb:
            return candidate
    return ramp[(index + x + y) % len(ramp)]


def repaint_cell(cell: Image.Image, hit_flash: bool = False, preserve_damage: bool = True) -> Image.Image:
    source = cell.convert("RGB")
    output = source.copy()
    for y in range(CELL):
        for x in range(CELL):
            rgb = source.getpixel((x, y))
            if not preserve_damage and is_intact_light(rgb):
                continue
            if is_centre_lamp(x, y, rgb):
                continue
            shell = is_shell_armour(rgb) if preserve_damage else (
                not is_green(rgb) and not is_intact_light(rgb) and not is_canopy(rgb)
                and 10.0 <= hsv(rgb)[0] * 360.0 <= 82.0
                and rgb[0] >= rgb[1] * 1.04 and rgb[1] >= rgb[2] * 1.18
            )
            if shell:
                output.putpixel((x, y), changed_ramp_colour(ARMOUR_RAMP, rgb, x, y))
            elif is_scute(rgb):
                output.putpixel((x, y), changed_ramp_colour(SCUTE_RAMP, rgb, x, y))
            elif hit_flash and is_hit_flash_material(rgb):
                output.putpixel((x, y), changed_ramp_colour(ARMOUR_RAMP, rgb, x, y))
            elif is_plate_highlight(source, x, y) and preserve_damage:
                output.putpixel((x, y), ARMOUR_RAMP[-1])
    # No resampling blur: the approved drawing is enlarged cell-by-cell.
    return clean_magenta(output).resize((OUT_CELL, OUT_CELL), Image.Resampling.NEAREST)


def build(reference_name: str, output_name: str, indices: tuple[int, ...]) -> None:
    reference = Image.open(ROOT / reference_name).convert("RGB")
    if reference.size != (REF_SIZE, REF_SIZE):
        raise SystemExit(f"{reference_name}: expected 1536x1536, got {reference.size}")
    output = Image.new("RGB", (OUT_SIZE, OUT_SIZE), GREEN)
    for index in indices:
        x0 = (index % 3) * CELL
        y0 = (index // 3) * CELL
        cell = reference.crop((x0, y0, x0 + CELL, y0 + CELL))
        painted = repaint_cell(
            cell,
            hit_flash=(index == 8),
            preserve_damage=(reference_name != "skinref_state0.png"),
        )
        output.paste(painted, ((index % 3) * OUT_CELL, (index // 3) * OUT_CELL))
    output.save(ROOT / output_name, format="PNG", optimize=False)


if __name__ == "__main__":
    build("skinref_state0.png", "skin_Bone_state0.png", tuple(range(9)))
    build("skinref_state1.png", "skin_Bone_state1.png", (0,))
    build("skinref_state2.png", "skin_Bone_state2.png", (0,))
