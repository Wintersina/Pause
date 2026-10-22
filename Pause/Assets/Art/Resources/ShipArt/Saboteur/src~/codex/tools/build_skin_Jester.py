#!/usr/bin/env python3
"""Build the Jester Saboteur sheets from the approved flipbook references.

The image-generation candidates were used as the visual repaint guide.  This
finishing pass is deliberately conservative: it starts from each approved
reference raster, paints only the Jester's authored teal livery bands, and
keeps every other pixel (including the approved damage and hit-flash art)
unchanged.  The final export is a 2x nearest-neighbour enlargement.
"""

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)

# Jester livery ramp, dark to light.  These are painted as hard pixel bands;
# they are not produced by a continuous hue rotation.
JESTER_SHADOW = np.array([15, 94, 106], dtype=np.uint8)   # #0F5E6A
JESTER_BASE = np.array([31, 181, 185], dtype=np.uint8)    # #1FB5B9
JESTER_HIGHLIGHT = np.array([110, 242, 238], dtype=np.uint8)  # #6EF2EE


def hsv(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Vectorised RGB -> HSV conversion; hue is in degrees."""
    f = rgb.astype(np.float32) / 255.0
    r, g, b = f[..., 0], f[..., 1], f[..., 2]
    mx = np.max(f, axis=2)
    mn = np.min(f, axis=2)
    delta = mx - mn
    h = np.zeros_like(mx)
    nz = delta > 1e-6
    with np.errstate(divide="ignore", invalid="ignore"):
        rmax = nz & (mx == r)
        gmax = nz & (mx == g)
        bmax = nz & (mx == b)
        h[rmax] = ((g - b) / delta)[rmax] % 6.0
        h[gmax] = ((b - r) / delta + 2.0)[gmax]
        h[bmax] = ((r - g) / delta + 4.0)[bmax]
    h *= 60.0
    s = np.divide(delta, mx, out=np.zeros_like(mx), where=mx > 1e-6)
    return h, s, mx


def non_green(rgb: np.ndarray) -> np.ndarray:
    r, g, b = rgb.astype(np.int16).transpose(2, 0, 1)
    return ~((g > 180) & (g > r * 1.6) & (g > b * 1.6))


def teal_core(rgb: np.ndarray) -> np.ndarray:
    r, g, b = rgb.astype(np.int16).transpose(2, 0, 1)
    return (b > 100) & (g > 80) & (b > r * 1.35) & (g > r * 1.15) & ~(
        (g > 180) & (g > r * 1.6) & (g > b * 1.6)
    )


def armour_pixels(rgb: np.ndarray, hit_flash: bool) -> np.ndarray:
    """Return paintable armour, excluding ink, lights, and damage FX."""
    h, s, value = hsv(rgb)
    r, g, b = rgb.astype(np.float32).transpose(2, 0, 1)
    if hit_flash:
        # The final cell is bone-white; it receives only the Jester stripe
        # treatment while its neutral material and damage drawing stay intact.
        material = (value > 0.30) & (s < 0.22)
    else:
        # Violet armour ramp.  This excludes the red caps, amber sparks,
        # blue/teal core glass, and the near-black selective ink.
        material = (h >= 245.0) & (h <= 345.0) & (s >= 0.18) & (value > 0.10)
        material &= b >= g * 1.02
    material &= non_green(rgb)
    material &= ~teal_core(rgb)
    return material


def chevron_masks(cx: float, cy: float, width: int, height: int, thickness: int,
                  count: int, spacing: int) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Create hard-edged shadow/base/highlight masks for a V stack."""
    size = (512, 512)
    shadow = Image.new("1", size, 0)
    base = Image.new("1", size, 0)
    highlight = Image.new("1", size, 0)
    ds = ImageDraw.Draw(shadow)
    db = ImageDraw.Draw(base)
    dh = ImageDraw.Draw(highlight)

    for index in range(count):
        top = int(round(cy + index * (height + spacing)))
        points = [(int(round(cx - width)), top),
                  (int(round(cx)), top + height),
                  (int(round(cx + width)), top)]
        # Offset layers give the new livery a discrete painted ramp: dark
        # lower-right footing, teal body, and a short upper-left rim light.
        ds.line(points, fill=1, width=thickness + 5, joint="curve")
        db.line([(x, y - 1) for x, y in points], fill=1, width=thickness, joint="curve")
        left = [points[0], points[1]]
        dh.line([(x, y - 2) for x, y in left], fill=1, width=max(2, thickness // 4), joint="curve")

    return (np.asarray(shadow, dtype=bool),
            np.asarray(base, dtype=bool),
            np.asarray(highlight, dtype=bool))


def horn_mask(cx: float, y: float, width: int, height: int, thickness: int) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Create one short downward V stripe for a horn base."""
    size = (512, 512)
    shadow = Image.new("1", size, 0)
    base = Image.new("1", size, 0)
    highlight = Image.new("1", size, 0)
    points = [(int(round(cx - width)), int(round(y))),
              (int(round(cx)), int(round(y + height))),
              (int(round(cx + width)), int(round(y)))]
    ImageDraw.Draw(shadow).line(points, fill=1, width=thickness + 4, joint="curve")
    ImageDraw.Draw(base).line([(x, yy - 1) for x, yy in points], fill=1, width=thickness, joint="curve")
    ImageDraw.Draw(highlight).line(
        [(x, yy - 2) for x, yy in points[:2]],
        fill=1,
        width=max(2, thickness // 4),
        joint="curve",
    )
    return (np.asarray(shadow, dtype=bool),
            np.asarray(base, dtype=bool),
            np.asarray(highlight, dtype=bool))


def paint_cell(cell: np.ndarray, cell_index: int) -> np.ndarray:
    """Paint one 512x512 pose without changing its occupied silhouette."""
    out = cell.copy()
    occupied = non_green(cell)
    paintable = armour_pixels(cell, hit_flash=(cell_index == 8))

    core = teal_core(cell)
    ys, xs = np.where(occupied)
    if core.any():
        core_y, core_x = np.where(core)
        cx = float(np.median(core_x))
        cy = float(np.median(core_y))
    else:
        cx = float(np.median(xs))
        cy = float(np.median(ys))

    # Three broad, evenly-spaced chevrons on the hub below the crystal.
    hit_tune = cell_index == 8
    stack = chevron_masks(
        cx,
        cy + 43.0,
        width=64 if hit_tune else 66,
        height=20 if hit_tune else 20,
        thickness=17 if hit_tune else 17,
        count=3,
        spacing=4,
    )

    # One short matching V at the base of each horn.  The bbox-relative
    # anchors follow the two bank poses without moving the approved ship.
    left_horn = horn_mask(float(xs.min() + 64), float(ys.min() + 137), 30 if hit_tune else 31, 15, 15 if hit_tune else 15)
    right_horn = horn_mask(float(xs.max() - 64), float(ys.min() + 137), 30 if hit_tune else 31, 15, 15 if hit_tune else 15)

    shadow = stack[0] | left_horn[0] | right_horn[0]
    base = stack[1] | left_horn[1] | right_horn[1]
    highlight = stack[2] | left_horn[2] | right_horn[2]

    # Clip every layer to existing armour pixels.  This makes silhouette
    # difference exactly zero and leaves damage, FX, core glass, and ink alone.
    shadow &= paintable
    base &= paintable
    highlight &= paintable

    out[shadow] = JESTER_SHADOW
    out[base] = JESTER_BASE
    out[highlight] = JESTER_HIGHLIGHT
    return out


def build(state: int) -> None:
    ref = np.asarray(Image.open(ROOT / f"skinref_state{state}.png").convert("RGB"))
    painted = np.empty_like(ref)
    for row in range(3):
        for col in range(3):
            y0, y1 = row * 512, (row + 1) * 512
            x0, x1 = col * 512, (col + 1) * 512
            painted[y0:y1, x0:x1] = paint_cell(ref[y0:y1, x0:x1], row * 3 + col)

    final = Image.fromarray(painted, "RGB").resize((3072, 3072), Image.Resampling.NEAREST)
    final.save(ROOT / f"skin_Jester_state{state}.png", format="PNG", optimize=False)


if __name__ == "__main__":
    for state in range(3):
        build(state)
