"""Build the Signal repaint from the approved Paranoid flipbook references.

The source sheets are deliberately kept at their native 512 px cell scale while
the repaint is applied. The final 2x nearest-neighbour upscale is the last step,
so no soft edge pixels or semi-transparent overlays are introduced.
"""

from __future__ import annotations

import colorsys
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
MAGENTA = (255, 0, 255)
INK = (5, 6, 12)
OUT_SIZE = 1536 * 2

# Signal's material ramp. The requested exact livery colours are included as
# discrete steps; the source luminance chooses which step is used inside the
# band, retaining the plate relief without using a hue-shift filter.
SIGNAL_RAMP = (
    (150, 55, 12),   # deep amber shadow
    (215, 95, 25),   # warm orange shadow
    (242, 134, 43),  # requested lower-right edge strip
    (255, 180, 60),  # requested signal amber
    (255, 200, 106), # amber highlight
    (244, 234, 212), # requested upper-left highlight
)

BONE_RAMP = (
    (190, 185, 176),
    (210, 204, 193),
    (226, 220, 207),
    (244, 234, 212),
    (255, 249, 229),
)


def green_armour(pixel: tuple[int, int, int]) -> bool:
    """Return true for diffuse green/olive armour, not ink, glass or FX."""
    r, g, b = pixel
    if pixel == MAGENTA or cyan_canopy(pixel):
        return False
    h, s, _ = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
    # The looser lower ramp catches the dark olive plate relief that is still
    # armour. It leaves red caps, amber nozzles, cyan glass and black ink alone.
    return 0.08 <= h <= 0.55 and s >= 0.02 and g >= r and g >= b * 0.85


def cyan_canopy(pixel: tuple[int, int, int]) -> bool:
    r, g, b = pixel
    return r < 100 and g > 95 and b > 100 and b >= g * 0.88 and b > r * 1.6


def luminance(pixel: tuple[int, int, int]) -> float:
    r, g, b = pixel
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def sanitize_dark_purple_ink(cell: Image.Image) -> None:
    """Remove near-black purple quantisation from the inherited ink edge."""
    pixels = cell.load()
    for y in range(512):
        for x in range(512):
            p = pixels[x, y]
            if p == MAGENTA:
                continue
            r, g, b = p
            h, s, v = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
            if v <= 0.25 and s > 0.5 and 280.0 / 360.0 <= h <= 345.0 / 360.0:
                pixels[x, y] = INK


def ramp_colour(value: float, ramp: tuple[tuple[int, int, int], ...]) -> tuple[int, int, int]:
    # Six hard steps, with no interpolated colours.
    lo, hi = 45.0, 220.0
    t = max(0.0, min(1.0, (value - lo) / (hi - lo)))
    index = min(len(ramp) - 1, int(round(t * (len(ramp) - 1))))
    return ramp[index]


def paint_signal_cell(cell: Image.Image, state: int, row: int, col: int) -> Image.Image:
    pixels = cell.load()
    if row == 2 and col == 2:
        # The hit-flash is intentionally still bone-white. Repaint only the
        # bright bone armour with a stepped bone ramp; ink, machinery and the
        # background remain untouched.
        for y in range(512):
            for x in range(512):
                p = pixels[x, y]
                r, g, b = p
                if p != MAGENTA and r > 125 and g > 125 and b > 120 and max(p) - min(p) < 45:
                    pixels[x, y] = ramp_colour(luminance(p), BONE_RAMP)
        return cell

    # d = y-x is the normal coordinate for an exact 45-degree upper-left to
    # lower-right stripe. A 70 px perpendicular stripe at source scale is
    # roughly 140 px in the requested final cell.
    for y in range(512):
        for x in range(512):
            p = pixels[x, y]
            if not green_armour(p):
                continue
            d = y - x
            if -50 <= d <= 50:
                # Paint the new material from the old value, not by hue shift.
                if d <= -45:
                    q = (244, 234, 212)       # 6 px final warm-bone edge
                elif d >= 40:
                    q = (242, 134, 43)        # 20 px final darker edge
                else:
                    q = ramp_colour(luminance(p), SIGNAL_RAMP)
                pixels[x, y] = q
    return cell


def build(state: int) -> None:
    source = Image.open(ROOT / f"skinref_state{state}.png").convert("RGB")
    sheet = Image.new("RGB", (OUT_SIZE, OUT_SIZE), MAGENTA)
    for row in range(3):
        for col in range(3):
            cell = source.crop((col * 512, row * 512, (col + 1) * 512, (row + 1) * 512))
            cell = paint_signal_cell(cell, state, row, col)
            sanitize_dark_purple_ink(cell)
            cell = cell.resize((1024, 1024), Image.Resampling.NEAREST)
            sheet.paste(cell, (col * 1024, row * 1024))
    sheet.save(ROOT / f"skin_Signal_state{state}.png", format="PNG", optimize=False)


if __name__ == "__main__":
    for state in range(3):
        build(state)
        print(f"wrote skin_Signal_state{state}.png")
