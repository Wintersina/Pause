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

# Night armour ramp.  These are deliberately discrete hand-painted steps,
# not a hue rotation: the source value selects a new indigo material step.
ARMOUR_RAMP = (
    (26, 31, 69),    # #1A1F45 shadow
    (35, 40, 86),
    (42, 46, 107),   # #2A2E6B base
    (58, 67, 126),
    (82, 94, 157),
    (116, 133, 194),
    (214, 239, 255),
)
SCUTE_RAMP = (
    (52, 59, 103),
    (70, 78, 132),
    (90, 98, 168),   # #5A62A8 base
    (119, 130, 194),
    (170, 190, 226),
    (220, 238, 255),
)
CYAN_RIM = (110, 242, 238)  # #6EF2EE


def is_green(rgb: tuple[int, int, int]) -> bool:
    return rgb == GREEN


def hsv(rgb: tuple[int, int, int]) -> tuple[float, float, float]:
    r, g, b = rgb
    return colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)


def is_shell_armour(rgb: tuple[int, int, int], allow_warm_edge: bool = False) -> bool:
    """Select tan-brown plate pixels, excluding pale scutes and emissive FX."""
    r, g, b = rgb
    if is_green(rgb) or r < 38 or g < 16 or b > 160:
        return False
    h, s, v = hsv(rgb)
    hue = h * 360.0
    # The approved reference's brown/gold ramp lives here.  Keep the very
    # saturated orange emitters out of the material repaint; they are damage
    # effects or the lamp, not armour.
    lower_hue = 15.0 if allow_warm_edge else 15.0
    # Bright orange/red damage is emissive FX; low-value brown in this same
    # hue band is still armour and must receive the Night repaint.
    bright_warm_fx = hue < 25.0 and s > 0.78 and v > 0.55
    return (lower_hue <= hue <= 65.0 and s >= 0.25
            and not bright_warm_fx
            and not (s > 0.90 and v > 0.88))


def is_scute(rgb: tuple[int, int, int]) -> bool:
    """Select pale sand scutes, excluding cyan glass, red lights and amber FX."""
    r, g, b = rgb
    if is_green(rgb) or r < 132 or g < 120 or b < 70:
        return False
    h, s, v = hsv(rgb)
    hue = h * 360.0
    return (r >= g - 4 and r - g <= 38 and g >= b + 7
            and s <= 0.52 and 12.0 <= hue <= 72.0 and v >= 0.52)


def ramp_colour(ramp: tuple[tuple[int, int, int], ...], rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    value = max(rgb) / 255.0
    # Hard, painterly bands with a few source-value buckets for texture.
    index = min(len(ramp) - 1, int(value * len(ramp)))
    return ramp[index]


def is_canopy(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    return g >= 95 and b >= 95 and b >= r + 28 and g >= r + 18


def is_magenta(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    h, s, _ = hsv(rgb)
    return s > 0.5 and 295.0 <= h * 360.0 <= 345.0


def clean_magenta(image: Image.Image) -> Image.Image:
    """Remove inherited pink/purple fringe without changing the silhouette."""
    source = image.copy().convert("RGB")
    output = image.copy().convert("RGB")
    for y in range(CELL):
        for x in range(CELL):
            if not is_magenta(source.getpixel((x, y))):
                continue
            replacement = None
            for radius in range(1, 5):
                for ox, oy in ((-radius, 0), (radius, 0), (0, -radius), (0, radius),
                               (-radius, -radius), (radius, -radius),
                               (-radius, radius), (radius, radius)):
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


def is_centre_lamp(x: int, y: int, rgb: tuple[int, int, int]) -> bool:
    """Protect the small amber centre lamp while repainting bright armour."""
    r, g, b = rgb
    h, s, v = hsv(rgb)
    return (230 <= x <= 282 and 260 <= y <= 316 and 27.0 <= h * 360.0 <= 42.0
            and s >= 0.68 and v <= 0.86 and r >= 120 and g >= 55 and b < 80)


def has_upper_left_edge(src: Image.Image, x: int, y: int, predicate) -> bool:
    """Find an existing plate boundary on its top/left-facing side."""
    for ox, oy in ((-1, 0), (0, -1), (-1, -1), (1, -1), (-1, 1)):
        nx, ny = x + ox, y + oy
        if 0 <= nx < CELL and 0 <= ny < CELL and not predicate(src.getpixel((nx, ny))):
            return True
    return False


def repaint_cell(cell: Image.Image, hit_flash: bool = False,
                 allow_warm_edge: bool = False) -> Image.Image:
    """Paint a cell from the generated Night ramp while retaining its pixels."""
    if hit_flash:
        # Repaint the neutral bone-white material with a fresh bone ramp while
        # leaving the cyan canopy and all saturated emitters unchanged.
        source = cell.copy().convert("RGB")
        output = source.copy()
        for y in range(CELL):
            for x in range(CELL):
                original = source.getpixel((x, y))
                r, g, b = original
                if (max(original) < 40 or is_canopy(original)
                        or max(original) - min(original) > 30):
                    continue
                value = max(original) / 255.0
                if value < 0.35:
                    target = (88, 88, 92)
                elif value < 0.55:
                    target = (150, 149, 146)
                elif value < 0.75:
                    target = (204, 201, 193)
                elif value < 0.90:
                    target = (238, 232, 214)
                else:
                    target = (255, 250, 232)
                output.putpixel((x, y), target)
        return clean_magenta(output).resize((OUT_CELL, OUT_CELL), Image.Resampling.NEAREST)

    source = cell.copy().convert("RGB")
    output = cell.copy().convert("RGB")

    # Material bases first.  Every selection is based on the reference pixel,
    # so panel seams, cracks, glints, canopy, lights, and silhouette remain.
    for y in range(CELL):
        for x in range(CELL):
            original = source.getpixel((x, y))
            if is_shell_armour(original, allow_warm_edge) and not is_centre_lamp(x, y, original):
                output.putpixel((x, y), ramp_colour(ARMOUR_RAMP, original))
            elif is_scute(original):
                output.putpixel((x, y), ramp_colour(SCUTE_RAMP, original))

    # Cyan edge light is painted only inside existing armour/scute boundaries;
    # no pixels are added outside the original silhouette and no halo is made.
    for y in range(CELL):
        for x in range(CELL):
            original = source.getpixel((x, y))
            material = ((is_shell_armour(original, allow_warm_edge)
                         and not is_centre_lamp(x, y, original))
                        or is_scute(original))
            if not material or max(original) < 115:
                continue
            if has_upper_left_edge(
                    source, x, y,
                    lambda p: is_shell_armour(p, allow_warm_edge) or is_scute(p)):
                # Keep the rim selective and broken like hand-placed pixel art.
                if (x + 2 * y) % 5 in (0, 1):
                    output.putpixel((x, y), CYAN_RIM)
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
            # The intact reference has no damage FX; its bank-right cell needs
            # the warmest brown edge pixels included in the new armour ramp.
            allow_warm_edge=(reference_name == "skinref_state0.png"),
        )
        output.paste(painted, ((index % 3) * OUT_CELL, (index // 3) * OUT_CELL))
    output.save(ROOT / output_name, format="PNG", optimize=False)


if __name__ == "__main__":
    build("skinref_state0.png", "skin_Night_state0.png", tuple(range(9)))
    build("skinref_state1.png", "skin_Night_state1.png", (0,))
    build("skinref_state2.png", "skin_Night_state2.png", (0,))
