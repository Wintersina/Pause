#!/usr/bin/env python3
"""Build the geometry-locked NeonComet Night colourway sheets.

The approved flipbook is the source of truth for cell geometry.  This pass uses a
hand-painted, quantised Night material ramp (guided by the generated repaint
candidates) only on crimson armour pixels; the source canopy, lights, tip diamonds,
ink, and effects remain pixel-identical.  The final export is nearest-neighbour
scaled 2x to the requested 1024px cells.
"""

from __future__ import annotations

import colorsys
from collections import deque
from pathlib import Path

from PIL import Image, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
SIZE = 1536
CELL = 512
OUT_SIZE = 3072
GREEN = (0, 255, 0)

REFS = [ROOT / f"skinref_state{i}.png" for i in range(3)]
OUTS = [ROOT / f"skin_Night_state{i}.png" for i in range(3)]


def is_warm_armour(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    if max(rgb) < 18 or (r, g, b) == GREEN:
        return False
    h, sat, _ = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    return ((h <= 0.12 or h >= 0.94) and sat >= 0.035 and r >= g + 1 and r >= b + 1)


def is_orange_effect(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    return r >= 90 and g >= r * 0.45 and g >= b * 1.35


def tip_diamond_mask(image: Image.Image) -> Image.Image:
    """Protect the small red portions of the two red/white wingtip diamonds."""
    px = image.load()
    mask = Image.new("L", image.size, 0)
    out = mask.load()
    for cy in range(3):
        for cx in range(3):
            x0, y0 = cx * CELL, cy * CELL
            seen: set[tuple[int, int]] = set()
            for y in range(y0 + 220, y0 + 480):
                for x in range(x0, x0 + CELL):
                    if (x, y) in seen:
                        continue
                    r, g, b = px[x, y]
                    if not (r >= 205 and g >= 185 and b >= 160):
                        continue
                    queue = deque([(x, y)])
                    seen.add((x, y))
                    points: list[tuple[int, int]] = []
                    while queue:
                        xx, yy = queue.popleft()
                        points.append((xx, yy))
                        for nx, ny in ((xx - 1, yy), (xx + 1, yy), (xx, yy - 1), (xx, yy + 1)):
                            if not (x0 <= nx < x0 + CELL and y0 + 220 <= ny < y0 + 480):
                                continue
                            if (nx, ny) in seen:
                                continue
                            nr, ng, nb = px[nx, ny]
                            if nr >= 205 and ng >= 185 and nb >= 160:
                                seen.add((nx, ny))
                                queue.append((nx, ny))
                    min_x = min(xx for xx, _ in points) - x0
                    max_x = max(xx for xx, _ in points) - x0
                    min_y = min(yy for _, yy in points) - y0
                    max_y = max(yy for _, yy in points) - y0
                    centre_x = (min_x + max_x) / 2
                    if len(points) <= 300 and (centre_x < 95 or centre_x > 417):
                        for yy in range(max(y0, y0 + min_y - 6), min(y0 + CELL, y0 + max_y + 7)):
                            for xx in range(max(x0, x0 + min_x - 6), min(x0 + CELL, x0 + max_x + 7)):
                                out[xx, yy] = 255
    return mask


def night_ramp(rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    """Map warm armour value into a discrete indigo/cyan material ramp."""
    r, g, b = rgb
    value = max(rgb)
    # The source's crimson base is often very bright but still has a low green
    # channel.  Keep those broad planes navy; reserve cyan for warm highlights.
    if g < 58:
        if value < 42:
            return (0x1A, 0x1F, 0x45)
        if value < 132:
            return (0x20, 0x26, 0x52)
        if value < 214:
            return (0x2A, 0x2E, 0x6B)
        if value < 246:
            return (0x38, 0x49, 0x85)
        return (0x6E, 0xF2, 0xEE)
    if value < 42:
        return (0x1A, 0x1F, 0x45)
    if value < 78:
        return (0x20, 0x26, 0x52)
    if value < 122:
        return (0x2A, 0x2E, 0x6B)
    if value < 166:
        return (0x38, 0x49, 0x85)
    if value < 214:
        return (0x4C, 0x70, 0xA0)
    if value < 242:
        return (0x6E, 0xF2, 0xEE)
    return (0xA8, 0xE9, 0xEA)


def build(index: int, clean: Image.Image) -> Image.Image:
    image = Image.open(REFS[index]).convert("RGB")
    clean_px = clean.load()
    source_px = image.load()
    tip_mask = tip_diamond_mask(image).load()
    result = image.copy()
    result_px = result.load()

    for y in range(SIZE):
        for x in range(SIZE):
            if (y // CELL, x // CELL) == (2, 2):
                continue
            rgb = source_px[x, y]
            if tip_mask[x, y]:
                continue
            if not is_warm_armour(rgb):
                continue
            # New orange tears/sparks/embers are effects, not armour plating.
            if index and is_orange_effect(rgb) and not is_orange_effect(clean_px[x, y]):
                continue
            result_px[x, y] = night_ramp(rgb)

    return result.resize((OUT_SIZE, OUT_SIZE), Image.Resampling.NEAREST)


def main() -> int:
    clean = Image.open(REFS[0]).convert("RGB")
    for index, out_path in enumerate(OUTS):
        build(index, clean).save(out_path, format="PNG", optimize=False)
        print(out_path.name)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
