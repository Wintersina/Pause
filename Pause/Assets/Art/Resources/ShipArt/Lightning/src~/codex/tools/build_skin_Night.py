#!/usr/bin/env python3
"""Build the Night flipbook from the approved sheets and generated repaint candidates.

The generated candidates supply the new plate lighting.  The approved sheets remain the
geometry authority: green pixels and every non-yellow pixel are copied verbatim, then the
three source sheets are enlarged 2x with nearest-neighbour for the requested 3072 export.
"""

from __future__ import annotations

import colorsys
import sys
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
SOURCES = [ROOT / f"skinref_state{i}.png" for i in range(3)]
OUTPUTS = [ROOT / f"skin_Night_state{i}.png" for i in range(3)]
GENERATED = [
    # Alternate generated candidates selected by lowest silhouette drift against each source.
    Path("/Users/sina/.codex/generated_images/01a1648a-a946-7fb3-bf4a-f3e8ab50da8b/exec-ee011169-520d-4285-abda-52ed9f59df1e.png"),
    Path("/Users/sina/.codex/generated_images/01a1648a-a946-7fb3-bf4a-f3e8ab50da8b/exec-c5b4af0a-9cd5-4adc-8741-cedc6f28eae0.png"),
    Path("/Users/sina/.codex/generated_images/01a1648a-a946-7fb3-bf4a-f3e8ab50da8b/exec-52d78137-9509-41e9-b5b6-0331ae709914.png"),
]

GREEN = (0, 255, 0)
CELL = 512

# The Night plate ramp is intentionally discrete and blue-indigo rather than a hue-shifted
# yellow ramp.  The cyan steps are reserved for the generated candidate's edge-light signal.
RAMP = (
    (17, 23, 53),    # seam/deep shadow
    (26, 31, 69),    # specified shadow
    (42, 46, 107),   # specified base
    (69, 77, 151),   # mid highlight
    (104, 142, 202), # upper plate highlight
    (110, 242, 238), # specified cyan rim light
    (224, 251, 255), # sparse pale blue-white glint
)

BONE_RAMP = (
    (198, 193, 181),
    (220, 214, 201),
    (237, 231, 216),
    (248, 242, 226),
)


def rgb_luma(rgb: tuple[int, int, int]) -> float:
    r, g, b = rgb
    return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0


def is_green(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    return g >= 235 and r <= 25 and b <= 25


def is_armour_yellow(rgb: tuple[int, int, int]) -> bool:
    """Select the lemon armour while leaving amber, red, white, and blue parts alone."""
    if is_green(rgb):
        return False
    r, g, b = rgb
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    # Armour is the saturated lemon band.  Amber nozzles/damage are redder (larger R-G).
    return 0.085 < h < 0.205 and s > 0.24 and v > 0.16 and (r - g) < 58


def candidate_is_plate(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    if g > 175 and r < 50 and b < 80:
        return False  # candidate's green field
    return b > r + 8 and b >= g - 18 and max(rgb) > 28


def candidate_sample(candidate: Image.Image, x: int, y: int, col: int, row: int) -> tuple[int, int, int]:
    """Read a corresponding generated cell, allowing for its 1254px raster size."""
    cw = candidate.width / 3.0
    ch = candidate.height / 3.0
    lx = (x - col * CELL + 0.5) / CELL
    ly = (y - row * CELL + 0.5) / CELL
    sx = min(candidate.width - 1, max(0, int((col + lx) * cw)))
    sy = min(candidate.height - 1, max(0, int((row + ly) * ch)))
    return candidate.getpixel((sx, sy))


def repaint_hit_flash(rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    """Keep the protected hit frame bone-white, with a fresh warm value ramp."""
    r, g, b = rgb
    if r < 150 or g < 140 or b < 120 or max(rgb) - min(rgb) >= 90:
        return rgb
    value = rgb_luma(rgb)
    if value < 0.58:
        return BONE_RAMP[0]
    if value < 0.74:
        return BONE_RAMP[1]
    if value < 0.90:
        return BONE_RAMP[2]
    return BONE_RAMP[3]


def ramp_colour(source_rgb: tuple[int, int, int], generated_rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    """Quantise source/candidate lighting into the Night ramp.

    The candidate contributes the repaint's local lighting and cyan edge cues; the source
    contributes the approved plate's per-pixel value, so no silhouette or panel geometry is
    inferred from the generative candidate.
    """
    source_v = rgb_luma(source_rgb)
    if candidate_is_plate(generated_rgb):
        generated_v = rgb_luma(generated_rgb)
        # Generated armour often has a darker blue value than the yellow source.  Lift it into
        # a stable ramp domain while retaining its local bright/dark ordering.
        generated_v = max(0.0, min(1.0, generated_v * 1.35))
        value = source_v * 0.42 + generated_v * 0.58
        gr, gg, gb = generated_rgb
        gh, gs, gv = colorsys.rgb_to_hsv(gr / 255, gg / 255, gb / 255)
        cyan_signal = (0.48 < gh < 0.62 and gs > 0.20 and gv > 0.60 and generated_v > 0.58)
    else:
        value = source_v
        cyan_signal = False

    if value < 0.16:
        index = 0
    elif value < 0.30:
        index = 1
    elif value < 0.48:
        index = 2
    elif value < 0.67:
        index = 3
    elif value < 0.84:
        index = 4
    else:
        index = 5
    if cyan_signal and index >= 4:
        index = 5
    # Keep white-hot source pixels from turning into broad white armour; only the generated
    # candidate's strongest local glint can reach the pale blue-white step.
    if index == 5 and candidate_is_plate(generated_rgb) and rgb_luma(generated_rgb) > 0.83:
        index = 6
    return RAMP[index]


def build_one(source_path: Path, generated_path: Path, output_path: Path) -> None:
    source = Image.open(source_path).convert("RGB")
    generated = Image.open(generated_path).convert("RGB")
    if source.size != (1536, 1536):
        raise ValueError(f"{source_path.name}: expected 1536x1536, got {source.size}")
    pixels = source.load()
    out = source.copy()
    out_pixels = out.load()

    for y in range(source.height):
        row = y // CELL
        for x in range(source.width):
            col = x // CELL
            rgb = pixels[x, y]
            if row == 2 and col == 2:
                out_pixels[x, y] = repaint_hit_flash(rgb)
                continue
            if is_armour_yellow(rgb):
                grgb = candidate_sample(generated, x, y, col, row)
                out_pixels[x, y] = ramp_colour(rgb, grgb)

    # The approved sheet's flat field is an exact chroma key; enforce it after the repaint.
    for y in range(source.height):
        for x in range(source.width):
            if is_green(pixels[x, y]):
                out_pixels[x, y] = GREEN

    final = out.resize((3072, 3072), Image.Resampling.NEAREST)
    final.save(output_path, format="PNG", optimize=False)
    print(f"wrote {output_path.name}: {final.size}")


def main() -> int:
    for source, generated, output in zip(SOURCES, GENERATED, OUTPUTS):
        if not source.exists():
            raise FileNotFoundError(source)
        if not generated.exists():
            raise FileNotFoundError(generated)
        build_one(source, generated, output)
    return 0


if __name__ == "__main__":
    sys.exit(main())
