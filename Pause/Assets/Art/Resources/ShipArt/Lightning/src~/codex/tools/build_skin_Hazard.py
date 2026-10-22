#!/usr/bin/env python3
"""Build the Hazard repaint from approved sheets plus generated repaint references.

The approved 512px sheets remain the geometry authority.  Generated candidates provide
local lighting guidance for the new yellow ramp; only the final anchored recolour and
the specified single livery band are raster-cleaned here.  Export is a strict 2x nearest
neighbour upscale to the requested 3072px sheets.
"""

from __future__ import annotations

import colorsys
import sys
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
CELL = 512
GREEN = (0, 255, 0)
SOURCES = [ROOT / f"skinref_state{i}.png" for i in range(3)]
GENERATED = [ROOT / f"candidate_Hazard_state{i}.png" for i in range(3)]
OUTPUTS = [ROOT / f"skin_Hazard_state{i}.png" for i in range(3)]

# Lemon-gold armour ramp: dark seam, base, mid, highlight, rim, hot edge.
ARMOUR_RAMP = (
    (92, 67, 8),
    (139, 105, 10),
    (184, 143, 13),
    (221, 184, 25),
    (245, 216, 49),
    (255, 238, 91),
)

# Exact livery colours requested by the brief.  The upper-left edge gets a one-pixel
# source-resolution highlight step; the lower-right edge is the deep shadow step.
BAND_SHADOW = (26, 26, 40)
BAND_BASE = (44, 45, 64)
BAND_HIGHLIGHT = (90, 92, 120)

BONE_RAMP = (
    (188, 184, 172),
    (220, 214, 201),
    (237, 231, 216),
    (248, 242, 226),
)


def is_green(rgb: tuple[int, int, int]) -> bool:
    return rgb == GREEN


def luma(rgb: tuple[int, int, int]) -> float:
    r, g, b = rgb
    return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0


def hsv(rgb: tuple[int, int, int]) -> tuple[float, float, float]:
    r, g, b = rgb
    return colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)


def is_blue_region(rgb: tuple[int, int, int]) -> bool:
    if is_green(rgb):
        return False
    h, s, v = hsv(rgb)
    return s > 0.18 and 0.47 < h < 0.72 and v > 0.08


def is_armour_yellow(rgb: tuple[int, int, int]) -> bool:
    if is_green(rgb):
        return False
    r, g, b = rgb
    h, s, v = hsv(rgb)
    # Excludes red bolts, amber nozzle light, cyan canopy, and white hit flash.
    return 0.085 < h < 0.205 and s > 0.24 and v > 0.16 and (r - g) < 58


def is_bolt_red(rgb: tuple[int, int, int]) -> bool:
    if is_green(rgb):
        return False
    h, s, v = hsv(rgb)
    return s > 0.45 and v > 0.15 and (h < 0.04 or h > 0.96)


def candidate_is_field(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    return g > 150 and g > r * 1.45 and g > b * 1.45


def candidate_sample(candidate: Image.Image, x: int, y: int, col: int, row: int) -> tuple[int, int, int]:
    # Generated sheets are 1254px, with a 418px logical cell.  Sample by normalized
    # cell coordinates so their lighting, not their small geometry drift, is retained.
    cw = candidate.width / 3.0
    ch = candidate.height / 3.0
    lx = (x - col * CELL + 0.5) / CELL
    ly = (y - row * CELL + 0.5) / CELL
    sx = min(candidate.width - 1, max(0, int((col + lx) * cw)))
    sy = min(candidate.height - 1, max(0, int((row + ly) * ch)))
    return candidate.getpixel((sx, sy))[:3]


def armour_colour(source_rgb: tuple[int, int, int], generated_rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    # Use the generated repaint only as a local light/shadow cue.  The approved source
    # value remains the stronger term, preventing generative geometry from leaking in.
    value = luma(source_rgb)
    if not candidate_is_field(generated_rgb):
        guide = luma(generated_rgb)
        value = value * 0.68 + guide * 0.32
    if value < 0.22:
        index = 0
    elif value < 0.36:
        index = 1
    elif value < 0.52:
        index = 2
    elif value < 0.68:
        index = 3
    elif value < 0.84:
        index = 4
    else:
        index = 5
    return ARMOUR_RAMP[index]


def bone_colour(source_rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    value = luma(source_rgb)
    if value < 0.16:
        return source_rgb
    if value < 0.42:
        return BONE_RAMP[0]
    if value < 0.64:
        return BONE_RAMP[1]
    if value < 0.84:
        return BONE_RAMP[2]
    return BONE_RAMP[3]


def band_distance(x: int, y: int) -> tuple[float, float]:
    # Cell coordinates are 512px here, equivalent to the requested 1024px line
    # through (200,860) and (860,200).  The source line is (100,430)-(430,100).
    signed = (x + y - 530) / 2**0.5
    return signed, abs(signed)


def band_colour(source_rgb: tuple[int, int, int], signed_distance: float) -> tuple[int, int, int]:
    # The band is 55 output px wide = 27.5 source px half-width.  Keep its upper-left
    # edge as a one-source-pixel highlight and its lower-right edge as the shadow step.
    if signed_distance < -26.0:
        return BAND_HIGHLIGHT
    if signed_distance > 26.0:
        return BAND_SHADOW
    # Preserve a small amount of hard local plate variation inside the dark base.
    return BAND_BASE if luma(source_rgb) >= 0.34 else BAND_SHADOW


def build_one(source_path: Path, generated_path: Path, output_path: Path, bolt_masks: list[list[list[bool]]]) -> None:
    source = Image.open(source_path).convert("RGB")
    generated = Image.open(generated_path).convert("RGB")
    if source.size != (1536, 1536):
        raise ValueError(f"{source_path.name}: expected 1536x1536, got {source.size}")

    out = source.copy()
    src_px = source.load()
    out_px = out.load()
    gen_px = generated.load()

    for y in range(source.height):
        row = y // CELL
        ly = y % CELL
        for x in range(source.width):
            col = x // CELL
            lx = x % CELL
            rgb = src_px[x, y]
            if is_green(rgb):
                out_px[x, y] = GREEN
                continue

            # The bottom-right cell is the protected hit flash.  Keep it bone-white but
            # refresh its hard value ramp so the cell is a real repaint, not a copy.
            if row == 2 and col == 2:
                out_px[x, y] = bone_colour(rgb)
                continue

            if is_armour_yellow(rgb):
                grgb = candidate_sample(generated, x, y, col, row)
                out_px[x, y] = armour_colour(rgb, grgb)

            signed, distance = band_distance(lx, ly)
            if distance <= 27.5:
                # Armour fills are painted over; red lightning bolts are included only
                # where the intact reference identifies the bolt, avoiding new coverage
                # over state-specific damage sparks, tears, or soot.
                bolt_here = bolt_masks[row][ly][col * CELL + lx]
                if is_armour_yellow(rgb) or (bolt_here and is_bolt_red(rgb)):
                    out_px[x, y] = band_colour(rgb, signed)

    final = out.resize((3072, 3072), Image.Resampling.NEAREST)
    final.save(output_path, format="PNG", optimize=False)
    print(f"wrote {output_path.name}: {final.size}")


def load_bolt_masks() -> list[list[list[bool]]]:
    ref = Image.open(SOURCES[0]).convert("RGB")
    px = ref.load()
    masks: list[list[list[bool]]] = []
    for row in range(3):
        rows: list[list[bool]] = []
        for y in range(row * CELL, (row + 1) * CELL):
            rows.append([is_bolt_red(px[x, y]) for x in range(1536)])
        masks.append(rows)
    return masks


def main() -> int:
    masks = load_bolt_masks()
    for source, generated, output in zip(SOURCES, GENERATED, OUTPUTS):
        if not source.exists() or not generated.exists():
            raise FileNotFoundError(source if not source.exists() else generated)
        build_one(source, generated, output, masks)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
