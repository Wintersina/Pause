from __future__ import annotations

import colorsys
import math
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
REF_SIZE = 1536
CELL = 512
OUT_CELL = 1024
OUT_SIZE = 3072


# A restrained Shellback brown ramp: it stays in the tan-brown family while
# giving the hull a painted, quantised ramp rather than a one-to-one hue shift.
SHELLBACK_RAMP = (
    (59, 37, 16),
    (86, 53, 19),
    (119, 75, 24),
    (157, 99, 30),
    (193, 130, 43),
    (222, 161, 65),
    (242, 194, 102),
)
SLATE = (44, 45, 64)
SLATE_DARK = (26, 26, 40)
SLATE_HIGHLIGHT = (90, 92, 120)


def is_green(rgb: tuple[int, int, int]) -> bool:
    return rgb == GREEN


def is_shell_armour(rgb: tuple[int, int, int]) -> bool:
    """Select tan-brown hull pixels, excluding cyan, pale scutes and FX."""
    r, g, b = rgb
    if is_green(rgb) or r < 45 or g < 18 or b > 135:
        return False
    h, s, v = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
    hue = h * 360.0
    # Brown/gold shell only. Orange damage, red lights and amber emissive FX
    # sit below this lower hue bound or are too saturated/bright.
    return 25.0 <= hue <= 57.0 and s >= 0.28 and v <= 0.92 and g < 205


def is_magenta(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    h, s, _ = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
    hue = h * 360.0
    return 295.0 <= hue <= 350.0 and s >= 0.28


def remove_magenta_fringe(image: Image.Image) -> Image.Image:
    """Clean the few inherited pink edge pixels without changing geometry."""
    source = image.copy()
    src = source.load()
    dst = image.load()
    for y in range(CELL):
        for x in range(CELL):
            if not is_magenta(src[x, y]):
                continue
            replacement = None
            for radius in range(1, 5):
                candidates = []
                for ox, oy in ((-radius, 0), (radius, 0), (0, -radius), (0, radius),
                               (-radius, -radius), (radius, -radius),
                               (-radius, radius), (radius, radius)):
                    nx, ny = x + ox, y + oy
                    if 0 <= nx < CELL and 0 <= ny < CELL:
                        colour = src[nx, ny]
                        if colour != GREEN and not is_magenta(colour):
                            candidates.append(colour)
                if candidates:
                    replacement = candidates[0]
                    break
            # A few reference pixels form isolated dark clusters with no
            # clean neighbour inside the search radius; use ink as the safe
            # local substitute so no pink/magenta survives the export.
            dst[x, y] = replacement if replacement is not None else (5, 6, 12)
    return image


def repaint_shell_armour(image: Image.Image) -> Image.Image:
    px = image.load()
    for y in range(CELL):
        for x in range(CELL):
            original = px[x, y]
            if not is_shell_armour(original):
                continue
            r, g, b = original
            value = max(r, g, b) / 255.0
            index = min(len(SHELLBACK_RAMP) - 1, int(value * len(SHELLBACK_RAMP)))
            target = SHELLBACK_RAMP[index]
            # Preserve the reference's small pixel-to-pixel texture while
            # replacing the material ramp with Shellback's warmer ramp.
            px[x, y] = tuple(round(old * 0.36 + new * 0.64)
                              for old, new in zip(original, target))
    return image


def repaint_hit_flash(image: Image.Image) -> Image.Image:
    """Keep the hit frame bone-white while giving it a fresh white ramp."""
    px = image.load()
    for y in range(CELL):
        for x in range(CELL):
            r, g, b = px[x, y]
            if (r, g, b) == GREEN:
                continue
            # Only warm pale surfaces; ink, gunmetal and any cyan pixels stay
            # exactly as approved in the reference.
            if r >= 125 and g >= 125 and b >= 105 and max(r, g, b) - min(r, g, b) <= 72:
                px[x, y] = (min(255, r + 2), min(255, g + 2), min(255, b + 1))
    return image


def ship_bbox(image: Image.Image) -> tuple[int, int, int, int]:
    points = [(x, y) for y in range(CELL) for x in range(CELL)
              if image.getpixel((x, y)) != GREEN]
    if not points:
        raise ValueError("reference cell has no ship")
    xs, ys = zip(*points)
    return min(xs), min(ys), max(xs), max(ys)


def draw_v(draw: ImageDraw.ImageDraw, apex: tuple[int, int], span: int,
           band: int, x_scale: float, shadow: bool = False) -> None:
    cx, ay = apex
    half = max(2, round(span * 0.5 * x_scale))
    run = max(2, round(half * math.tan(math.radians(40))))
    ey = ay + run
    offset = 1 if shadow else 0
    width = band + 2 if shadow else band
    left = (cx - half, ey + offset)
    tip = (cx, ay + offset)
    right = (cx + half, ey + offset)
    draw.line((left, tip), fill=255, width=width)
    draw.line((tip, right), fill=255, width=width)


def draw_highlight(draw: ImageDraw.ImageDraw, apex: tuple[int, int], span: int,
                   x_scale: float) -> None:
    cx, ay = apex
    half = max(2, round(span * 0.5 * x_scale))
    run = max(2, round(half * math.tan(math.radians(40))))
    ey = ay + run
    # One hard pixel on the top-facing edge of each arm.
    draw.line(((cx - half, ey - 1), (cx, ay - 1)), fill=255, width=1)
    draw.line(((cx, ay - 1), (cx + half, ey - 1)), fill=255, width=1)


def apply_chevrons(image: Image.Image, index: int) -> Image.Image:
    if index == 8:  # approved hit-flash cell remains bone-white
        return image
    base_mask = Image.new("L", (CELL, CELL), 0)
    shadow_mask = Image.new("L", (CELL, CELL), 0)
    highlight_mask = Image.new("L", (CELL, CELL), 0)
    base_draw = ImageDraw.Draw(base_mask)
    shadow_draw = ImageDraw.Draw(shadow_mask)
    highlight_draw = ImageDraw.Draw(highlight_mask)

    xmin, ymin, xmax, ymax = ship_bbox(image)
    ship_w = xmax - xmin + 1
    ship_h = ymax - ymin + 1
    cx = (xmin + xmax) // 2
    bank_scale = 0.74 if index in (6, 7) else 1.0
    if index == 6:
        cx -= round(ship_w * 0.012)
    elif index == 7:
        cx += round(ship_w * 0.012)

    band = max(2, round(ship_w * 0.06))
    for fraction, offset in ((0.34, 0.74), (0.28, 0.80), (0.22, 0.86)):
        apex = (cx, round(ymin + ship_h * offset))
        span = round(ship_w * fraction)
        draw_v(shadow_draw, apex, span, band, bank_scale, shadow=True)
        draw_v(base_draw, apex, span, band, bank_scale)
        draw_highlight(highlight_draw, apex, span, bank_scale)

    # Small, separate flank chevrons beside the upper scute.
    flank_y = round(ymin + ship_h * 0.38)
    flank_span = round(ship_w * 0.12)
    for side in (-1, 1):
        flank_x = cx + round(side * ship_w * 0.30)
        apex = (flank_x, flank_y)
        draw_v(shadow_draw, apex, flank_span, max(2, round(band * 0.72)),
               bank_scale, shadow=True)
        draw_v(base_draw, apex, flank_span, max(2, round(band * 0.72)),
               bank_scale)
        draw_highlight(highlight_draw, apex, flank_span, bank_scale)

    # Only paint on the original brown shell. This leaves ink seams, scutes,
    # canopy, lights, flippers and damage FX untouched; cracks therefore cut
    # naturally through the livery.
    shell_mask = Image.new("L", (CELL, CELL), 0)
    shell_px = shell_mask.load()
    source_px = image.load()
    for y in range(CELL):
        for x in range(CELL):
            if is_shell_armour(source_px[x, y]):
                shell_px[x, y] = 255

    for mask, colour in ((shadow_mask, SLATE_DARK),
                         (base_mask, SLATE),
                         (highlight_mask, SLATE_HIGHLIGHT)):
        mask = Image.composite(mask, Image.new("L", (CELL, CELL), 0), shell_mask)
        layer = Image.new("RGB", (CELL, CELL), colour)
        image.paste(layer, (0, 0), mask)
    return image


def process_cell(source: Image.Image, index: int) -> Image.Image:
    cell = source.crop((index % 3 * CELL, index // 3 * CELL,
                        (index % 3 + 1) * CELL, (index // 3 + 1) * CELL)).convert("RGB")
    remove_magenta_fringe(cell)
    # The hit frame remains bone-white, with only a restrained warm-white ramp
    # repaint so the colourway is still represented in every required cell.
    if index == 8:
        repaint_hit_flash(cell)
    else:
        repaint_shell_armour(cell)
        apply_chevrons(cell, index)
    return cell.resize((OUT_CELL, OUT_CELL), Image.Resampling.NEAREST)


def build(reference_name: str, output_name: str, indices: tuple[int, ...]) -> None:
    reference = Image.open(ROOT / reference_name).convert("RGB")
    if reference.size != (REF_SIZE, REF_SIZE):
        raise SystemExit(f"{reference_name}: expected 1536x1536, got {reference.size}")
    output = Image.new("RGB", (OUT_SIZE, OUT_SIZE), GREEN)
    for index in indices:
        cell = process_cell(reference, index)
        x = (index % 3) * OUT_CELL
        y = (index // 3) * OUT_CELL
        output.paste(cell, (x, y))
    output.save(ROOT / output_name, format="PNG", optimize=False)


if __name__ == "__main__":
    build("skinref_state0.png", "skin_Shellback_state0.png", tuple(range(9)))
    build("skinref_state1.png", "skin_Shellback_state1.png", (0,))
    build("skinref_state2.png", "skin_Shellback_state2.png", (0,))
