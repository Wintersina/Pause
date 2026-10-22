#!/usr/bin/env python3
"""Build Bone sheets from generated repaint candidates and locked references.

The generated candidates supply the Bone material/light ramp.  The approved
references supply the exact registration, pose geometry, and every non-armour
pixel, so the finished sheets retain the reference flipbook contract.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
GENERATED = Path("/Users/sina/.codex/generated_images/01a16494-e1d9-79d0-aa8c-382c41050aa3")
TARGETS = [
    (ROOT / "skinref_state0.png", GENERATED / "exec-e0cd1dea-4b8b-4d3b-a67f-5eba43247f2c.png", ROOT / "skin_Bone_state0.png"),
    (ROOT / "skinref_state1.png", GENERATED / "exec-2decaac1-d928-4695-bc59-52d3d9ab92ed.png", ROOT / "skin_Bone_state1.png"),
    (ROOT / "skinref_state2.png", GENERATED / "exec-cbb8e465-0499-45d2-b059-d9e2d56fcf49.png", ROOT / "skin_Bone_state2.png"),
]

GREEN = np.array([0, 255, 0], dtype=np.uint8)

# Warm ceramic ramp: shadow -> base -> edge light -> near-white highlight.
# These tones intentionally keep the hard, hand-painted banding of the generated
# candidates rather than applying a single hue rotation to the teal source.
BONE = np.array(
    [
        [0xB9, 0xAE, 0x98],  # #B9AE98 shadow
        [0xD7, 0xCC, 0xB9],  # warm mid
        [0xF4, 0xEA, 0xD4],  # #F4EAD4 base
        [0xFF, 0xF7, 0xE3],  # warm highlight
        [0xFF, 0xFD, 0xF4],  # near-white edge
    ],
    dtype=np.float32,
)


def luminance(rgb: np.ndarray) -> np.ndarray:
    return rgb.astype(np.float32) @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def bone_ramp(lum: np.ndarray) -> np.ndarray:
    """Map painted lightness to hard Bone material bands with slight interpolation."""
    stops = np.array([24.0, 62.0, 122.0, 186.0, 238.0], dtype=np.float32)
    idx = np.clip(np.searchsorted(stops, lum, side="right") - 1, 0, len(BONE) - 1)
    out = BONE[idx].copy()
    for i in range(len(BONE) - 1):
        m = idx == i
        if not np.any(m):
            continue
        t = np.clip((lum[m] - stops[i]) / max(stops[i + 1] - stops[i], 1.0), 0.0, 1.0)
        out[m] = BONE[i] * (1.0 - 0.26 * t[:, None]) + BONE[i + 1] * (0.26 * t[:, None])
    return np.rint(out).clip(0, 255).astype(np.uint8)


def cell_bounds(cell: int, size: int) -> tuple[int, int, int, int]:
    row, col = divmod(cell, 3)
    return row * size // 3, (row + 1) * size // 3, col * size // 3, (col + 1) * size // 3


def canopy_region(shape: tuple[int, int, int]) -> np.ndarray:
    """Central faceted glass silhouette, including its light-catching edge."""
    h, w, _ = shape
    points = [
        (round(0.50 * w), round(0.12 * h)),
        (round(0.61 * w), round(0.22 * h)),
        (round(0.62 * w), round(0.33 * h)),
        (round(0.54 * w), round(0.44 * h)),
        (round(0.50 * w), round(0.46 * h)),
        (round(0.46 * w), round(0.44 * h)),
        (round(0.38 * w), round(0.33 * h)),
        (round(0.39 * w), round(0.22 * h)),
    ]
    mask = Image.new("1", (w, h), 0)
    ImageDraw.Draw(mask).polygon(points, fill=1)
    return np.asarray(mask, dtype=bool)


def armour_mask(ref: np.ndarray, cell: int) -> np.ndarray:
    """Select teal ceramic plates while locking glass, lights, effects, and field."""
    p = ref.astype(np.int16)
    r, g, b = p[..., 0], p[..., 1], p[..., 2]
    non_green = np.any(p != GREEN.astype(np.int16), axis=-1)

    h, w, _ = ref.shape
    yy, xx = np.mgrid[0:h, 0:w]
    canopy_shape = canopy_region(ref.shape)

    # The canopy is the central faceted blue shape.  Blue shadows outside that
    # shape belong to the shell and must receive the Bone repaint too.
    blue_glass = non_green & canopy_shape & (b > g * 1.04) & (b > r * 1.25) & (g > 10)
    teal_armour = (
        non_green
        & (g > r * 0.10)
        & (b > r * 0.10)
        & (g > 2)
        & (b > 2)
        & (np.abs(b - g) < 160)
        & (yy < 0.77 * h)
    )

    bright_white = (r > 150) & (g > 150) & (b > 150)
    red_orange = (r > g * 1.12) & (r > b * 1.12)
    # Banked cells have less visible teal armour.  Refresh only their white
    # ceramic plate highlights outside the locked canopy; they remain near-white
    # Bone highlights, while the red/white emitters still read as lights.
    bank_plate_highlight = bright_white & ~canopy_shape & (cell in (6, 7))
    base = (teal_armour | bank_plate_highlight) & ~blue_glass & ~red_orange & ~canopy_shape
    if cell == 7:
        # A few near-black pixels are interior plate seams in the bank-right
        # pose, not outer silhouette ink. Refresh only seams touching at least
        # two already-selected ceramic pixels so the selective outline stays dark.
        dark = non_green & (np.maximum.reduce([r, g, b]) <= 3) & ~canopy_shape & (yy < 0.77 * h)
        neighbours = np.zeros_like(dark)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            neighbours |= np.roll(np.roll(base, dy, axis=0), dx, axis=1)
        base |= dark & neighbours
    return base


def hit_flash_armour_mask(cell: np.ndarray) -> np.ndarray:
    """Refresh the existing bone-white hit flash without touching its canopy/pod."""
    p = cell.astype(np.int16)
    r, g, b = p[..., 0], p[..., 1], p[..., 2]
    non_green = np.any(p != GREEN.astype(np.int16), axis=-1)
    spread = np.maximum.reduce([r, g, b]) - np.minimum.reduce([r, g, b])
    h, w, _ = cell.shape
    yy, xx = np.mgrid[0:h, 0:w]
    canopy_shape = canopy_region(cell.shape)
    tail = yy >= 0.77 * h
    warm_or_white = (r > 30) & (g > 28) & (b > 24) & (spread < 110)
    return non_green & warm_or_white & ~canopy_shape & ~tail & (r > 40) & (g > 38) & (b > 34)


def build(ref_path: Path, candidate_path: Path, output_path: Path) -> int:
    ref_image = Image.open(ref_path).convert("RGB")
    candidate_image = Image.open(candidate_path).convert("RGB")
    ref = np.asarray(ref_image)
    candidate = np.asarray(candidate_image.resize(ref_image.size, Image.Resampling.NEAREST))
    out = ref.copy()

    # The first eight cells are the actual teal armour poses.  Keep the
    # reference geometry and use both source and generated lightness so the
    # repaint has a new ceramic ramp, not a flat hue swap.
    for cell in range(8):
        y0, y1, x0, x1 = cell_bounds(cell, ref.shape[0])
        ref_cell = ref[y0:y1, x0:x1]
        candidate_cell = candidate[y0:y1, x0:x1]
        mask = armour_mask(ref_cell, cell)
        source_lum = luminance(ref_cell[mask])
        candidate_lum = luminance(candidate_cell[mask])
        lum = 0.62 * source_lum + 0.38 * candidate_lum
        out[y0:y1, x0:x1][mask] = bone_ramp(lum)

    # The reference hit-flash is already bone-white.  Refresh its warm ceramic
    # bands so it remains visibly part of this colourway while preserving the
    # cyan/white canopy, gunmetal pod, and pure-green background.
    y0, y1, x0, x1 = cell_bounds(8, ref.shape[0])
    ref_cell = ref[y0:y1, x0:x1]
    candidate_cell = candidate[y0:y1, x0:x1]
    mask = hit_flash_armour_mask(ref_cell)
    source_lum = luminance(ref_cell[mask])
    candidate_lum = luminance(candidate_cell[mask])
    out[y0:y1, x0:x1][mask] = bone_ramp(0.70 * source_lum + 0.30 * candidate_lum)

    Image.fromarray(out, "RGB").resize((3072, 3072), Image.Resampling.NEAREST).save(output_path, format="PNG", optimize=True)
    return int(np.any(out != ref, axis=-1).sum())


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--force", action="store_true", help="overwrite the Bone outputs produced by an earlier pass")
    args = parser.parse_args()
    for ref, candidate, output in TARGETS:
        if output.exists() and not args.force:
            raise SystemExit(f"refusing to overwrite existing file: {output}")
        if not candidate.exists():
            raise SystemExit(f"missing generated candidate: {candidate}")
        count = build(ref, candidate, output)
        print(f"{output.name}: changed native pixels = {count}")


if __name__ == "__main__":
    main()
