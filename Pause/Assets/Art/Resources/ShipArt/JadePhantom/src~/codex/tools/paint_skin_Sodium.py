"""Build the Sodium JadePhantom sheets from the approved reference sheets.

This is a pixel-art repaint pass, not a geometric renderer: the source sheet is
kept as the mask/texture authority, green armour pixels are quantised into a
warm sodium material ramp, and every other material is copied unchanged.
"""

from __future__ import annotations

import colorsys
from pathlib import Path

from PIL import Image


HERE = Path(__file__).resolve().parents[1]
BG = (0, 255, 0)

# Sodium metal ramp, darkest to hottest. The requested anchor colours are kept
# exact; the two intermediate tones preserve the source's small pixel texture.
SODIUM_RAMP = (
    (107, 44, 20),   # deep warm shadow
    (169, 72, 26),   # requested shadow #A9481A
    (217, 101, 34),  # copper transition
    (242, 134, 43),  # requested base #F2862B
    (255, 180, 60),  # requested highlight #FFB43C
    (255, 239, 184), # pale yellow-white hot edge
)
BONE_FLASH = (255, 243, 205)
BONE_RAMP = (
    (211, 202, 190),
    (235, 227, 211),
    (255, 246, 220),
)


def hsv(rgb: tuple[int, int, int]) -> tuple[float, float, float]:
    r, g, b = (v / 255.0 for v in rgb)
    return colorsys.rgb_to_hsv(r, g, b)


def is_armour_pixel(rgb: tuple[int, int, int]) -> bool:
    """Recognise JadePhantom's green armour, excluding canopy/effects/ink."""

    if rgb == BG:
        return False
    r, g, b = rgb
    h, s, v = hsv(rgb)
    # Jade armour is green/cyan-green. Blue canopy pixels are intentionally
    # excluded by the hue and blue-dominance checks.
    if not (0.20 <= h <= 0.52 and s >= 0.16 and v >= 0.08):
        return False
    if b > g * 1.08 and b > r * 1.35:
        return False
    # Very dark neutral ink is part of the drawing, not diffuse armour.
    if max(rgb) < 30 and s < 0.35:
        return False
    return True


def source_luma(rgb: tuple[int, int, int]) -> float:
    r, g, b = rgb
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def ramp_index(luma: float, lo: float, hi: float) -> int:
    # Quantise the source's local value range into six hard pixel-art bands.
    t = 0.0 if hi <= lo else max(0.0, min(1.0, (luma - lo) / (hi - lo)))
    return min(len(SODIUM_RAMP) - 1, int(t * len(SODIUM_RAMP)))


def repaint_flash(rgb: tuple[int, int, int]) -> tuple[int, int, int] | None:
    """Keep the hit-flash bone-white while cleaning its pale value ramp."""

    if rgb == BG or max(rgb) < 60:
        return None
    r, g, b = rgb
    _, saturation, _ = hsv(rgb)
    if saturation >= 0.30:
        return None
    value = source_luma(rgb)
    if value < 110:
        return BONE_RAMP[0]
    if value < 190:
        return BONE_RAMP[1]
    return BONE_RAMP[2]


def repaint(path: Path, out_path: Path) -> None:
    src = Image.open(path).convert("RGB")
    if src.size != (1536, 1536):
        raise ValueError(f"{path.name}: expected 1536x1536, got {src.size}")

    px = src.load()
    armour_values = [
        source_luma(px[x, y])
        for y in range(src.height)
        for x in range(src.width)
        if is_armour_pixel(px[x, y])
    ]
    if not armour_values:
        raise ValueError(f"{path.name}: no armour pixels found")
    lo, hi = min(armour_values), max(armour_values)

    dst = src.copy()
    out = dst.load()
    for y in range(src.height):
        for x in range(src.width):
            cell_col, cell_row = x // 512, y // 512
            rgb = px[x, y]
            if cell_col == 2 and cell_row == 2:
                flash = repaint_flash(rgb)
                if flash is not None:
                    out[x, y] = flash
                continue
            if not is_armour_pixel(rgb):
                continue
            out[x, y] = SODIUM_RAMP[ramp_index(source_luma(rgb), lo, hi)]

    # The project requires the high-resolution 2x nearest-neighbour export.
    final = dst.resize((3072, 3072), Image.Resampling.NEAREST)
    final.save(out_path, format="PNG", optimize=False)


def main() -> None:
    for state in range(3):
        source = HERE / f"skinref_state{state}.png"
        target = HERE / f"skin_Sodium_state{state}.png"
        repaint(source, target)
        print(target.name)


if __name__ == "__main__":
    main()
