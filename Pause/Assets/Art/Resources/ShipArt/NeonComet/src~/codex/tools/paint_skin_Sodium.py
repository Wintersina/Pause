#!/usr/bin/env python3
"""Paint the approved NeonComet flipbook into the Sodium colourway.

The generated Sodium candidates in the parent directory were used as the
visual paint guide.  The approved sheets remain the geometry stencil: only
crimson armour pixels are repainted, then the result is enlarged 2x with
nearest-neighbour so every pose keeps its original anchor and silhouette.
"""

from __future__ import annotations

import colorsys
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
REF_SIZE = 1536
CELL = 512
OUT_SIZE = 3072
GREEN = (0, 255, 0)
INK = (5, 6, 12)

REFS = [ROOT / f"skinref_state{i}.png" for i in range(3)]
OUTS = [ROOT / f"skin_Sodium_state{i}.png" for i in range(3)]

# Palette chosen from the generated Sodium repaint candidates and constrained
# to the requested warm sodium-lamp material ramp.
SODIUM_DEEP = (112, 43, 19)
SODIUM_SHADOW = (0xA9, 0x48, 0x1A)
SODIUM_BASE = (0xF2, 0x86, 0x2B)
SODIUM_HIGHLIGHT = (0xFF, 0xB4, 0x3C)
SODIUM_HOT = (0xFF, 0xF1, 0xB2)


def is_crimson(rgb: tuple[int, int, int]) -> bool:
    """Return true for diffuse crimson armour, excluding ink/blue/cream."""

    r, g, b = rgb
    if rgb == GREEN or max(rgb) < 18:
        return False
    h, sat, _ = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
    return sat >= 0.035 and (h <= 0.12 or h >= 0.94) and r >= g + 1 and r >= b + 1


def is_crimson_armour(rgb: tuple[int, int, int]) -> bool:
    """Tighter crimson test used on the intact geometry stencil."""

    r, g, b = rgb
    return is_crimson(rgb)


def is_wingtip_diamond(rgb: tuple[int, int, int]) -> bool:
    """Identify the red/cream wingtip diamond pixels, which stay unchanged."""

    r, g, b = rgb
    return r >= 205 and g >= 185 and b >= 160


def is_pale_cream(rgb: tuple[int, int, int]) -> bool:
    """Keep the reference's cream slash lights and white-hot material edges."""

    return min(rgb) >= 130 and colorsys.rgb_to_hsv(*(c / 255.0 for c in rgb))[1] < 0.24


def is_forbidden_magenta(rgb: tuple[int, int, int]) -> bool:
    """Catch source purple/pink edge noise while leaving cyan/blue untouched."""

    h, sat, _ = colorsys.rgb_to_hsv(*(c / 255.0 for c in rgb))
    return 0.80 <= h <= 0.95 and sat > 0.45


def diamond_protection(image: Image.Image) -> set[tuple[int, int]]:
    """Protect only the two small wingtip diamonds in each 512px cell."""

    px = image.load()
    protected: set[tuple[int, int]] = set()
    for row in range(3):
        for col in range(3):
            x0, y0 = col * CELL, row * CELL
            seen: set[tuple[int, int]] = set()
            for y in range(y0 + 245, y0 + 465):
                for x in range(x0, x0 + CELL):
                    if (x, y) in seen or not is_wingtip_diamond(px[x, y]):
                        continue
                    todo = [(x, y)]
                    seen.add((x, y))
                    points: list[tuple[int, int]] = []
                    while todo:
                        xx, yy = todo.pop()
                        points.append((xx, yy))
                        for nx, ny in ((xx - 1, yy), (xx + 1, yy), (xx, yy - 1), (xx, yy + 1)):
                            if not (x0 <= nx < x0 + CELL and y0 + 245 <= ny < y0 + 465):
                                continue
                            if (nx, ny) in seen or not is_wingtip_diamond(px[nx, ny]):
                                continue
                            seen.add((nx, ny))
                            todo.append((nx, ny))
                    if 40 <= len(points) <= 350:
                        min_x = min(xx for xx, _ in points)
                        max_x = max(xx for xx, _ in points)
                        min_y = min(yy for _, yy in points)
                        max_y = max(yy for _, yy in points)
                        # Tip diamonds are the small cream components near a
                        # cell edge; the larger slash lights are excluded.
                        near_edge = min_x - x0 < 80 or max_x - x0 > 432
                        narrow = max_x - min_x <= 32
                        if near_edge and narrow:
                            for yy in range(max(y0, min_y - 4), min(y0 + CELL, max_y + 5)):
                                for xx in range(max(x0, min_x - 4), min(x0 + CELL, max_x + 5)):
                                    protected.add((xx, yy))
    return protected


def sodium_ramp(rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    """Quantise the reference armour value into the Sodium painted ramp."""

    r, g, b = rgb
    # The source red channel is frequently saturated even in deep crimson;
    # perceived luminance keeps the orange base from washing out to cream.
    value = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0
    if value < 0.14:
        return SODIUM_DEEP
    if value < 0.25:
        return SODIUM_SHADOW
    if value < 0.38:
        return SODIUM_BASE
    if value < 0.58:
        return SODIUM_HIGHLIGHT
    return SODIUM_HOT


def is_orange_effect(rgb: tuple[int, int, int]) -> bool:
    """Detect the orange damage FX added only in damaged/wreck states."""

    r, g, b = rgb
    return r >= 90 and g >= r * 0.45 and g >= b * 1.35


def repaint(index: int, intact: Image.Image) -> Image.Image:
    source = Image.open(REFS[index]).convert("RGB")
    result = source.copy()
    source_px = source.load()
    intact_px = intact.load()
    result_px = result.load()
    protected = diamond_protection(intact)

    for y in range(REF_SIZE):
        for x in range(REF_SIZE):
            # Bottom-right is the approved bone-white hit flash.
            if (y // CELL, x // CELL) == (2, 2):
                continue
            current = source_px[x, y]
            if is_forbidden_magenta(current):
                result_px[x, y] = INK
                continue
            if (x, y) in protected or is_pale_cream(current):
                continue
            if not is_crimson(current):
                continue
            # New orange tears/sparks/embers in damaged states are effects,
            # not armour.  Preserve them when they do not exist in intact.
            if index and is_orange_effect(current) and not is_orange_effect(intact_px[x, y]):
                continue
            result_px[x, y] = sodium_ramp(current)

    return result.resize((OUT_SIZE, OUT_SIZE), Image.Resampling.NEAREST)


def main() -> int:
    intact = Image.open(REFS[0]).convert("RGB")
    for index, out_path in enumerate(OUTS):
        repaint(index, intact).save(out_path, format="PNG", optimize=False)
        print(out_path.name)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
