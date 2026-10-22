#!/usr/bin/env python3
"""Build Courier sheets from generated repaint guides and locked references.

The references own registration, silhouettes, panel geometry, lights, canopy and FX.
The generated candidates contribute painted value variation to the Courier ceramic
ramp; this keeps the repaint pixel-faithful without shipping generation drift.
"""

from __future__ import annotations

from pathlib import Path
import argparse

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
GENERATED = Path("/Users/sina/.codex/generated_images/01a164a0-68ff-7891-9f4b-c55ff1182a2d")
TARGETS = [
    (ROOT / "skinref_state0.png", GENERATED / "exec-3c971192-3097-416f-9ec4-63906469f7af.png", ROOT / "skin_Courier_state0.png"),
    (ROOT / "skinref_state1.png", GENERATED / "exec-2724a844-45eb-4e59-a98d-31d86e02d7b4.png", ROOT / "skin_Courier_state1.png"),
    (ROOT / "skinref_state2.png", GENERATED / "exec-4b3d2477-84f4-4143-b30c-a8da18cfbf71.png", ROOT / "skin_Courier_state2.png"),
]

GREEN = np.array([0, 255, 0], dtype=np.uint8)
CELL = 512

# Courier ceramic ramp, shadow -> near-white.  Values are deliberately discrete
# to retain the hard neon pixel-art bands after nearest-neighbour export.
BONE = np.array(
    [
        [0xB9, 0xAE, 0x98],  # requested shadow
        [0xD0, 0xC5, 0xB1],
        [0xF4, 0xEA, 0xD4],  # requested base
        [0xFF, 0xF7, 0xE5],
        [0xFF, 0xFD, 0xF5],  # near-white highlight
    ],
    dtype=np.float32,
)


def luminance(rgb: np.ndarray) -> np.ndarray:
    return rgb.astype(np.float32) @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def bone_ramp(values: np.ndarray) -> np.ndarray:
    """Quantise a generated painted value field into Courier ceramic bands."""
    stops = np.array([20.0, 58.0, 112.0, 176.0, 232.0], dtype=np.float32)
    idx = np.clip(np.searchsorted(stops, values, side="right") - 1, 0, len(BONE) - 1)
    out = BONE[idx].copy()
    # A small, bounded within-band blend retains the candidate's painted texture
    # while keeping the palette visibly ramp-based rather than hue-shifted.
    for i in range(len(BONE) - 1):
        selected = idx == i
        if not np.any(selected):
            continue
        t = np.clip((values[selected] - stops[i]) / max(stops[i + 1] - stops[i], 1.0), 0.0, 1.0)
        out[selected] = BONE[i] * (1.0 - 0.24 * t[:, None]) + BONE[i + 1] * (0.24 * t[:, None])
    return np.rint(out).clip(0, 255).astype(np.uint8)


def canopy_region(shape: tuple[int, int, int]) -> np.ndarray:
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


def split_line(cell: int, shape: tuple[int, int, int]) -> tuple[np.ndarray, np.ndarray]:
    """Return x coordinates of the centre spine and the hull-side selector.

    Poses 0-5 and the hit pose are upright.  Poses 6 and 7 are banked, so their
    division follows the visible nose-to-tail axis instead of using a screen-space
    vertical split.
    """
    h, w, _ = shape
    yy = np.arange(h, dtype=np.float32)[:, None]
    if cell == 6:  # bank left: nose is left of the tail
        top, bottom = 238.0, 224.0
    elif cell == 7:  # bank right: nose is right of the tail
        top, bottom = 270.0, 286.0
    else:
        top = bottom = 256.0
    spine = top + (bottom - top) * (yy / max(h - 1, 1))
    xx = np.arange(w, dtype=np.float32)[None, :]
    return spine, xx >= spine


def teal_armour_mask(ref: np.ndarray, cell: int) -> np.ndarray:
    """Select teal armour while excluding canopy, effects, outlines and tail pod."""
    p = ref.astype(np.int16)
    r, g, b = p[..., 0], p[..., 1], p[..., 2]
    non_green = np.any(p != GREEN.astype(np.int16), axis=-1)
    h, w, _ = ref.shape
    yy, _ = np.mgrid[0:h, 0:w]
    canopy = canopy_region(ref.shape)
    tail = yy >= round(0.77 * h)

    # Teal plates are green/blue dominant and carry real channel energy.  This
    # intentionally excludes the black selective outline, red lights and warm
    # brown/orange damage; the reference pixels for those remain locked.
    teal = (
        non_green
        & (g >= 18)
        & (b >= 18)
        & (g >= r * 1.08)
        & (b >= r * 1.08)
        & (np.maximum.reduce([r, g, b]) >= 34)
    )
    return teal & ~canopy & ~tail


def hit_flash_mask(ref: np.ndarray) -> np.ndarray:
    """Refresh existing ceramic pixels in the hit-flash without touching lights."""
    p = ref.astype(np.int16)
    r, g, b = p[..., 0], p[..., 1], p[..., 2]
    non_green = np.any(p != GREEN.astype(np.int16), axis=-1)
    h, w, _ = ref.shape
    yy, _ = np.mgrid[0:h, 0:w]
    canopy = canopy_region(ref.shape)
    tail = yy >= round(0.77 * h)
    spread = np.maximum.reduce([r, g, b]) - np.minimum.reduce([r, g, b])
    near_white_light = (r > 220) & (g > 220) & (b > 220)
    red_orange = (r > g * 1.15) & (r > b * 1.15)
    ceramic = (
        non_green
        & ~canopy
        & ~tail
        & ~near_white_light
        & ~red_orange
        & (r >= 80)
        & (g >= 70)
        & (b >= 55)
        & (spread <= 72)
    )
    return ceramic


def build(ref_path: Path, candidate_path: Path, output_path: Path) -> int:
    ref_image = Image.open(ref_path).convert("RGB")
    candidate_image = Image.open(candidate_path).convert("RGB")
    ref = np.asarray(ref_image)
    candidate = np.asarray(candidate_image.resize(ref_image.size, Image.Resampling.NEAREST))
    out = ref.copy()

    for cell in range(9):
        row, col = divmod(cell, 3)
        y0, y1 = row * CELL, (row + 1) * CELL
        x0, x1 = col * CELL, (col + 1) * CELL
        ref_cell = ref[y0:y1, x0:x1]
        cand_cell = candidate[y0:y1, x0:x1]
        if cell == 8:
            mask = hit_flash_mask(ref_cell)
            split = np.ones_like(mask, dtype=bool)
        else:
            mask = teal_armour_mask(ref_cell, cell)
            _, split = split_line(cell, ref_cell.shape)
            # The left side is intentionally byte-identical to the approved teal
            # reference.  Only the right-side armour receives the Courier ramp.
            mask &= split
        if not np.any(mask):
            continue
        source = luminance(ref_cell[mask])
        generated = cand_cell[mask]
        generated_rgb = generated.astype(np.int16)
        generated_green = (
            (generated_rgb[..., 0] < 24)
            & (generated_rgb[..., 1] > 185)
            & (generated_rgb[..., 2] < 24)
        )
        guide = luminance(generated)
        guide = np.where(generated_green, source, guide)
        values = 0.42 * source + 0.58 * guide
        out[y0:y1, x0:x1][mask] = bone_ramp(values)

    # Remove the tiny legacy purple-black fringe pixels present in the supplied
    # raster references.  Courier has no pink/magenta; these are dark outline
    # noise, so snapping them to ink preserves the silhouette and line weight.
    r, g, b = out[..., 0].astype(np.int16), out[..., 1].astype(np.int16), out[..., 2].astype(np.int16)
    dark_purple = (
        (r + b >= 4)
        & (r > g * 1.35)
        & (b > r * 0.55)
        & (b < r * 1.8)
        & ((np.maximum.reduce([r, g, b]) - np.minimum.reduce([r, g, b])) > np.maximum.reduce([r, g, b]) * 0.38)
        & (np.maximum(r, b) < 180)
    )
    out[dark_purple] = np.array([0, 0, 0], dtype=np.uint8)

    Image.fromarray(out, "RGB").resize((3072, 3072), Image.Resampling.NEAREST).save(
        output_path, format="PNG", optimize=True
    )
    return int(np.any(out != ref, axis=-1).sum())


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--force", action="store_true", help="replace Courier outputs made by an earlier pass")
    args = parser.parse_args()
    for ref, candidate, output in TARGETS:
        if output.exists() and not args.force:
            raise SystemExit(f"refusing to overwrite existing file: {output}")
        if not candidate.exists():
            raise SystemExit(f"missing generated repaint guide: {candidate}")
        count = build(ref, candidate, output)
        print(f"{output.name}: changed native pixels = {count}")


if __name__ == "__main__":
    main()
