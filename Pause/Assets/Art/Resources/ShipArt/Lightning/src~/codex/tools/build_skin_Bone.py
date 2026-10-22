#!/usr/bin/env python3
"""Build the Bone Lightning colourway from the approved 3x3 sheets.

The generated repaint candidates are used only as lighting/material references.  The approved
sheet remains the authority for every pixel of geometry, effects, and placement; this keeps the
colourway pixel-faithful while allowing a fresh warm ceramic ramp rather than a hue shift.
"""

from __future__ import annotations

import colorsys
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
CELL = 512
GREEN = (0, 255, 0)
SOURCES = [ROOT / f"skinref_state{i}.png" for i in range(3)]
OUTPUTS = [ROOT / f"skin_Bone_state{i}.png" for i in range(3)]

# Selected generated repaint candidates.  They supply new plate value structure, not geometry.
GENERATED = [
    Path("/Users/sina/.codex/generated_images/01a1649c-bf76-7a23-a073-d82114075860/exec-3cbfb658-7045-40d6-b6ff-307912b5156c.png"),
    Path("/Users/sina/.codex/generated_images/01a1649c-bf76-7a23-a073-d82114075860/exec-fd0e18c7-663e-4c63-8b69-05885b475c3e.png"),
    Path("/Users/sina/.codex/generated_images/01a1649c-bf76-7a23-a073-d82114075860/exec-26acb296-8ca7-4e5b-8a58-4850cda977c2.png"),
]

# Dark seam -> base -> warm highlight -> near-white rim.  The two specified anchors are exact.
BONE_RAMP = (
    (116, 106, 91),    # dark warm seam/shadow
    (185, 174, 152),   # specified shadow #B9AE98
    (214, 203, 183),   # ceramic mid
    (244, 234, 212),   # specified base #F4EAD4
    (250, 245, 230),   # warm highlight
    (255, 252, 242),   # near-white top-left rim
)


def is_green(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    return g >= 235 and r <= 25 and b <= 25


def luma(rgb: tuple[int, int, int]) -> float:
    r, g, b = rgb
    return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0


def hsv(rgb: tuple[int, int, int]) -> tuple[float, float, float]:
    r, g, b = rgb
    return colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)


def is_blue_or_cyan(rgb: tuple[int, int, int]) -> bool:
    if is_green(rgb):
        return False
    h, s, v = hsv(rgb)
    return s > 0.18 and 0.47 < h < 0.72 and v > 0.08


def is_red_or_amber(rgb: tuple[int, int, int]) -> bool:
    # Preserve the approved red bolts, wingtip diamonds and orange damage.  Saturated lemon
    # armour can also have R-G > 22, so hue—not channel difference alone—separates it.
    h, s, v = hsv(rgb)
    return s > 0.28 and (h < 0.075 or h > 0.95)


def is_armour_pixel(rgb: tuple[int, int, int]) -> bool:
    """Select yellow armour and its pale yellow highlights, excluding protected materials."""
    if is_green(rgb) or is_blue_or_cyan(rgb) or is_red_or_amber(rgb):
        return False
    r, g, b = rgb
    # Non-black neutral seam pixels are part of the ceramic plate treatment.
    if 10 <= max(rgb) < 80 and max(rgb) - min(rgb) <= 18:
        return True
    if max(rgb) < 42:
        return False  # ink, sockets, and deep blue-black seams remain exact
    h, s, v = hsv(rgb)
    # Saturated lemon/yellow plate pixels.
    if 0.075 < h < 0.205 and s > 0.18 and g >= r * 0.52:
        return True
    # Cream/yellow highlight pixels have low saturation but retain the warm ordering R >= G >= B.
    if r >= g >= b and r - b <= 66 and v > 0.56 and g > 120:
        return True
    return False


def is_interior_plate_seam(pixels, x: int, y: int) -> bool:
    """Include dark warm seam pixels, but never repaint the outer ink boundary."""
    r, g, b = pixels[x, y]
    if not (3 <= max(r, g, b) < 42):
        return False
    h, s, v = hsv((r, g, b))
    if s > 0.18 and 0.47 < h < 0.72 and v > 0.08:
        return False  # canopy/blue region stays byte-for-byte identical
    if b > r + 4 and b > g + 3:
        return False  # deep blue structure
    if r > g + 22 and r > b + 24:
        return False  # dark red/orange damage or nozzle material
    for yy in (-1, 0, 1):
        for xx in (-1, 0, 1):
            rr, gg, bb = pixels[x + xx, y + yy]
            if is_green((rr, gg, bb)):
                return False
    return True


def candidate_sample(candidate: Image.Image, x: int, y: int, col: int, row: int) -> tuple[int, int, int]:
    """Read the corresponding generated cell without changing the approved cell geometry."""
    cw = candidate.width / 3.0
    ch = candidate.height / 3.0
    lx = (x - col * CELL + 0.5) / CELL
    ly = (y - row * CELL + 0.5) / CELL
    sx = min(candidate.width - 1, max(0, int((col + lx) * cw)))
    sy = min(candidate.height - 1, max(0, int((row + ly) * ch)))
    return candidate.getpixel((sx, sy))


def guide_value(source_rgb: tuple[int, int, int], generated_rgb: tuple[int, int, int]) -> float:
    """Blend approved local value with generated ceramic lighting, then quantise to the ramp."""
    source_value = luma(source_rgb)
    if not is_green(generated_rgb) and not is_blue_or_cyan(generated_rgb) and not is_red_or_amber(generated_rgb):
        generated_value = luma(generated_rgb)
        # The candidates are brighter ceramic renders; retain their local bands without letting
        # the generated image erase the approved sheet's fine texture.
        return source_value * 0.44 + generated_value * 0.56
    return source_value


def ramp_colour(source_rgb: tuple[int, int, int], generated_rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    value = guide_value(source_rgb, generated_rgb)
    if value < 0.18:
        index = 0
    elif value < 0.32:
        index = 1
    elif value < 0.49:
        index = 2
    elif value < 0.68:
        index = 3
    elif value < 0.84:
        index = 4
    else:
        index = 5
    return BONE_RAMP[index]


def distinct_ramp_colour(source_rgb: tuple[int, int, int], generated_rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    """Avoid leaving a source pixel byte-identical when it is part of the repaint."""
    value = guide_value(source_rgb, generated_rgb)
    if value < 0.18:
        index = 0
    elif value < 0.32:
        index = 1
    elif value < 0.49:
        index = 2
    elif value < 0.68:
        index = 3
    elif value < 0.84:
        index = 4
    else:
        index = 5
    for offset in (0, 1, -1, 2, -2, 3, -3, 4, -4, 5):
        candidate = BONE_RAMP[max(0, min(5, index + offset))]
        if candidate != source_rgb:
            return candidate
    return BONE_RAMP[index]


def repaint_hit_flash(source_rgb: tuple[int, int, int], generated_rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    """Give the white hit frame a fresh warm ceramic ramp while leaving ink/effects alone."""
    if is_green(source_rgb) or is_blue_or_cyan(source_rgb) or is_red_or_amber(source_rgb):
        return source_rgb
    if luma(source_rgb) < 0.19:
        return source_rgb
    return distinct_ramp_colour(source_rgb, generated_rgb)


def build_one(source_path: Path, generated_path: Path, output_path: Path) -> None:
    source = Image.open(source_path).convert("RGB")
    generated = Image.open(generated_path).convert("RGB")
    if source.size != (1536, 1536):
        raise ValueError(f"{source_path.name}: expected 1536x1536, got {source.size}")

    out = source.copy()
    sp = source.load()
    op = out.load()
    for y in range(source.height):
        row = y // CELL
        for x in range(source.width):
            col = x // CELL
            original = sp[x, y]
            if is_green(original):
                op[x, y] = GREEN
                continue
            guide = candidate_sample(generated, x, y, col, row)
            if row == 2 and col == 2:
                op[x, y] = repaint_hit_flash(original, guide)
            elif is_armour_pixel(original) or is_interior_plate_seam(sp, x, y):
                op[x, y] = distinct_ramp_colour(original, guide)
            else:
                op[x, y] = original

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
    raise SystemExit(main())
