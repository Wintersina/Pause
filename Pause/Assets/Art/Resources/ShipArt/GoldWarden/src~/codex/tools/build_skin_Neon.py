#!/usr/bin/env python3
"""Build the Neon GoldWarden sheets from reference geometry and AI paint guidance."""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image


GREEN = np.array([0, 255, 0], dtype=np.uint8)
TEAL_RAMP = np.array(
    [
        [6, 34, 42],       # deep teal-black seam-adjacent shade
        [15, 94, 106],     # shadow
        [31, 181, 185],    # requested base
        [54, 211, 210],    # enamel mid highlight
        [110, 242, 238],   # requested highlight
        [229, 255, 249],   # mint edge light
    ],
    dtype=np.uint8,
)
BLUE_RAMP = np.array(
    [
        [10, 31, 112],
        [23, 63, 181],
        [43, 107, 255],    # requested canopy core
        [92, 153, 255],
        [232, 250, 255],
    ],
    dtype=np.uint8,
)


def hsv(rgb: np.ndarray) -> np.ndarray:
    return np.asarray(Image.fromarray(rgb, mode="RGB").convert("HSV"), dtype=np.uint8)


def ramp_index(value: np.ndarray, count: int) -> np.ndarray:
    # Hard bands preserve the source's pixel-art character.
    return np.clip((value.astype(np.int16) * count) // 256, 0, count - 1)


def paint_sheet(reference_path: Path, guidance_path: Path, output_path: Path) -> None:
    ref = np.asarray(Image.open(reference_path).convert("RGB"), dtype=np.uint8)
    guidance_small = Image.open(guidance_path).convert("RGB")
    guidance = np.asarray(guidance_small.resize((1536, 1536), Image.Resampling.NEAREST), dtype=np.uint8)
    out = ref.copy()

    ref_hsv = hsv(ref)
    guide_hsv = hsv(guidance)
    ref_h = ref_hsv[..., 0].astype(np.float32) * 360.0 / 255.0
    ref_s = ref_hsv[..., 1].astype(np.float32) / 255.0
    ref_v = ref_hsv[..., 2].astype(np.float32) / 255.0
    guide_h = guide_hsv[..., 0].astype(np.float32) * 360.0 / 255.0
    guide_s = guide_hsv[..., 1].astype(np.float32) / 255.0
    guide_v = guide_hsv[..., 2].astype(np.float32) / 255.0

    yy, xx = np.indices((1536, 1536))
    cell_x = xx % 512
    cell_y = yy % 512
    non_green = np.any(ref != GREEN, axis=2)

    # AI guidance identifies which source planes were painted as teal. The
    # warm-guide exclusion keeps orange damage and embers intact, while the
    # explicit gold band guarantees no gold armour is left behind.
    source_warm = (ref_h >= 10) & (ref_h <= 90) & (ref_s > 0.05) & (ref_v > 0.12)
    guide_teal = (guide_h >= 145) & (guide_h <= 205) & (guide_s > 0.18)
    guide_warm = ((guide_h < 70) | (guide_h > 330)) & (guide_s > 0.30) & (guide_v > 0.18)
    gold_band = (ref_h >= 35) & (ref_h <= 55) & (ref_s > 0.50)
    armour = non_green & ((source_warm & ~guide_warm) | gold_band | guide_teal)

    # Do not repaint the small engine-bell/nozzle glow bands. They are part of
    # the approved ship lighting, not armour.
    nozzle = (
        (((cell_x >= 130) & (cell_x <= 210)) | ((cell_x >= 302) & (cell_x <= 382)))
        & (cell_y >= 310)
        & (cell_y <= 430)
    )
    armour &= ~nozzle

    # Transfer the generated repaint's local value into a new, explicit teal
    # ramp. This is a painted-ramp remap, not a hue rotation: the generated
    # candidate controls the banding and the new palette is quantised hard.
    teal_value = np.clip(0.62 * guide_v + 0.38 * ref_v, 0.0, 1.0)
    teal_idx = ramp_index((teal_value * 255).astype(np.uint8), len(TEAL_RAMP))
    out[armour] = TEAL_RAMP[teal_idx[armour]]

    # The canopy is isolated by the blue paint in the guidance image and by
    # the top-center glass envelope in each cell. This leaves all gunmetal,
    # red marks, damage, and white hit-flash pixels untouched.
    top_center = (cell_y <= 245) & (cell_x >= 135) & (cell_x <= 380)
    source_glass = non_green & top_center & (ref_h >= 125) & (ref_h <= 235) & (ref_s > 0.18)
    guide_blue = (guide_h >= 205) & (guide_h <= 270) & (guide_s > 0.28)
    canopy = source_glass & guide_blue
    blue_idx = ramp_index((np.clip(0.60 * guide_v + 0.40 * ref_v, 0.0, 1.0) * 255).astype(np.uint8), len(BLUE_RAMP))
    out[canopy] = BLUE_RAMP[blue_idx[canopy]]

    # The wreck's hit-flash remains bone-white, but gets a fresh bone-white
    # ramp so the colourway is visibly repainted even in its flash frame.
    if reference_path.name == "skinref_state2.png":
        flash = (xx // 512 == 2) & (yy // 512 == 2)
        flash_light = flash & non_green & (ref_v > 0.38) & (ref_s < 0.30)
        bone_idx = ramp_index((np.clip(ref_v * 255.0, 0, 255)).astype(np.uint8), 3)
        bone_ramp = np.array([[189, 183, 174], [232, 225, 213], [255, 247, 229]], dtype=np.uint8)
        out[flash_light] = bone_ramp[bone_idx[flash_light]]

    # The reference is already a flat pure green key. Restore it explicitly so
    # no generated background pixel can leak into the final sheet.
    out[~non_green] = GREEN

    final = Image.fromarray(out, mode="RGB").resize((3072, 3072), Image.Resampling.NEAREST)
    final.save(output_path, format="PNG", optimize=False)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("reference", type=Path)
    ap.add_argument("guidance", type=Path)
    ap.add_argument("output", type=Path)
    args = ap.parse_args()
    paint_sheet(args.reference, args.guidance, args.output)


if __name__ == "__main__":
    main()
