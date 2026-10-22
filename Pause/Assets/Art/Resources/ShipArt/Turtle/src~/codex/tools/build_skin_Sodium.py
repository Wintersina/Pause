from __future__ import annotations

import colorsys
import math
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
REF_SIZE = 1536
CELL = 512
OUT_CELL = 1024
OUT_SIZE = 3072

# These ramps are taken from the requested Sodium brief and the image-generated
# candidate repaint. They are deliberately stepped rather than derived from a
# single hue rotation: each source value band gets a new painted material tone.
SODIUM_SHELL = (
    (91, 39, 16),       # deep socket-side orange-brown
    (169, 72, 26),      # shadow #A9481A
    (207, 94, 28),
    (242, 134, 43),     # base #F2862B
    (255, 180, 60),     # highlight #FFB43C
    (255, 239, 166),    # pale yellow-white hot edge
)
SODIUM_SCUTE = (
    (155, 116, 66),
    (211, 164, 101),
    (255, 208, 138),    # light amber #FFD08A
    (255, 225, 169),
    (255, 244, 204),
)
HIT_BONE = (
    (126, 119, 111),
    (177, 168, 153),
    (218, 207, 188),
    (244, 235, 213),
    (255, 251, 226),
)


def hsv(rgb: tuple[int, int, int]) -> tuple[float, float, float]:
    r, g, b = rgb
    return colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)


def non_green(rgb: tuple[int, int, int]) -> bool:
    return rgb != GREEN


def is_shell_armour(rgb: tuple[int, int, int]) -> bool:
    """Select the warm shell metal, excluding scutes, lights, FX and canopy."""
    if not non_green(rgb):
        return False
    h, s, v = hsv(rgb)
    degrees = h * 360.0
    # The approved shell is orange/gold. Red player lamps and orange damage
    # tears sit below this hue band; cyan/blue hardware is far outside it.
    return 18.0 <= degrees <= 59.0 and s >= 0.28 and v <= 0.98


def is_scute(rgb: tuple[int, int, int]) -> bool:
    """Select pale sand scutes without touching cyan glass or red lamps."""
    if not non_green(rgb):
        return False
    r, g, b = rgb
    h, s, v = hsv(rgb)
    degrees = h * 360.0
    return (
        25.0 <= degrees <= 62.0
        and s <= 0.56
        and v >= 0.46
        and b >= 78
        and r >= g - 8
    )


def is_hit_pale(rgb: tuple[int, int, int]) -> bool:
    """Bone-white material in the approved hit-flash cell."""
    if not non_green(rgb):
        return False
    r, g, b = rgb
    h, s, v = hsv(rgb)
    degrees = h * 360.0
    # Keep the dark ink, gunmetal, red lamps and any cyan unchanged. The hit
    # flash's pale hull/scute planes get a fresh warm bone ramp.
    return v >= 0.42 and s <= 0.34 and not (150.0 <= degrees <= 260.0)


def is_forbidden_magenta(rgb: tuple[int, int, int]) -> bool:
    if not non_green(rgb):
        return False
    h, s, _ = hsv(rgb)
    return 295.0 <= h * 360.0 <= 350.0 and s >= 0.28


def clean_forbidden_magenta(cell: Image.Image) -> None:
    """Remove dark pink fringe without changing the ship's geometry."""
    source = cell.copy()
    src = source.load()
    dst = cell.load()
    for y in range(CELL):
        for x in range(CELL):
            if not is_forbidden_magenta(src[x, y]):
                continue
            replacement = None
            for radius in range(1, 5):
                for ox, oy in ((-radius, 0), (radius, 0), (0, -radius), (0, radius),
                               (-radius, -radius), (radius, -radius),
                               (-radius, radius), (radius, radius)):
                    nx, ny = x + ox, y + oy
                    if 0 <= nx < CELL and 0 <= ny < CELL:
                        candidate = src[nx, ny]
                        if non_green(candidate) and not is_forbidden_magenta(candidate):
                            replacement = candidate
                            break
                if replacement is not None:
                    break
            dst[x, y] = replacement if replacement is not None else (5, 6, 12)


def ramp_pick(rgb: tuple[int, int, int], ramp: tuple[tuple[int, int, int], ...]) -> tuple[int, int, int]:
    r, g, b = rgb
    _, _, value = hsv(rgb)
    # Source value is remapped into a new hand-painted ramp. The lower clamp
    # preserves the dark material facets while the upper bands create the
    # sodium hot-edge accents visible in the generated repaint candidate.
    normalized = max(0.0, min(1.0, (value - 0.16) / 0.78))
    index = min(len(ramp) - 1, int(normalized * len(ramp)))
    return ramp[index]


def cell_bbox(cell: Image.Image) -> tuple[int, int, int, int]:
    points = [
        (x, y)
        for y in range(CELL)
        for x in range(CELL)
        if cell.getpixel((x, y)) != GREEN
    ]
    if not points:
        raise ValueError("reference cell is empty")
    xs, ys = zip(*points)
    return min(xs), min(ys), max(xs), max(ys)


def protect_center_lamp(x: int, y: int, bbox: tuple[int, int, int, int]) -> bool:
    """Leave the small amber centre lamp exactly as approved."""
    xmin, ymin, xmax, ymax = bbox
    cx = (xmin + xmax) * 0.5
    cy = ymin + (ymax - ymin) * 0.575
    return abs(x - cx) <= 20 and abs(y - cy) <= 20


def repaint_cell(
    source: Image.Image,
    index: int,
    damage_base: Image.Image | None = None,
) -> Image.Image:
    cell = source.crop((index % 3 * CELL, index // 3 * CELL,
                        (index % 3 + 1) * CELL, (index // 3 + 1) * CELL)).convert("RGB")
    base = damage_base.crop((0, 0, CELL, CELL)).convert("RGB") if damage_base else None
    bbox = cell_bbox(cell)
    pixels = cell.load()
    base_pixels = base.load() if base else None

    for y in range(CELL):
        for x in range(CELL):
            original = pixels[x, y]
            if not non_green(original) or protect_center_lamp(x, y, bbox):
                continue
            # In damaged/wreck key poses, the approved state-0 key pose is the
            # clean underlay. If a pixel differs there, it belongs to a damage
            # overlay/crack/soot region and must stay exactly as drawn.
            if base_pixels is not None and original != base_pixels[x, y]:
                # A small colour drift is part of the armour's repaintable
                # shading. Large deviations are the approved damage FX and
                # remain pixel-for-pixel unchanged.
                delta = math.sqrt(sum((a - b) ** 2 for a, b in zip(original, base_pixels[x, y])))
                if delta > 15.0:
                    continue
            if index == 8:
                if is_hit_pale(original):
                    pixels[x, y] = ramp_pick(original, HIT_BONE)
            elif is_shell_armour(original):
                pixels[x, y] = ramp_pick(original, SODIUM_SHELL)
            elif is_scute(original):
                pixels[x, y] = ramp_pick(original, SODIUM_SCUTE)
    clean_forbidden_magenta(cell)
    return cell.resize((OUT_CELL, OUT_CELL), Image.Resampling.NEAREST)


def build(reference_name: str, output_name: str, indices: tuple[int, ...], damage_base_name: str | None = None) -> None:
    reference = Image.open(ROOT / reference_name).convert("RGB")
    if reference.size != (REF_SIZE, REF_SIZE):
        raise SystemExit(f"{reference_name}: expected 1536x1536, got {reference.size}")
    base = Image.open(ROOT / damage_base_name).convert("RGB") if damage_base_name else None
    output = Image.new("RGB", (OUT_SIZE, OUT_SIZE), GREEN)
    for index in indices:
        cell = repaint_cell(reference, index, base if index == 0 else None)
        output.paste(cell, ((index % 3) * OUT_CELL, (index // 3) * OUT_CELL))
    output.save(ROOT / output_name, format="PNG", optimize=False)


if __name__ == "__main__":
    build("skinref_state0.png", "skin_Sodium_state0.png", tuple(range(9)))
    build("skinref_state1.png", "skin_Sodium_state1.png", (0,), "skinref_state0.png")
    build("skinref_state2.png", "skin_Sodium_state2.png", (0,), "skinref_state0.png")
