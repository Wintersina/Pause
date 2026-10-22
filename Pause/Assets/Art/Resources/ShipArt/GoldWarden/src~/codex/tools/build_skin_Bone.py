#!/usr/bin/env python3
"""Build the Bone colourway from generated repaint candidates and locked references."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.uint8)
REFERENCES = tuple(ROOT / f"skinref_state{i}.png" for i in range(3))
CANDIDATES = tuple(ROOT / "generated_candidates" / f"skin_Bone_state{i}_gen.png" for i in range(3))
OUTPUTS = tuple(ROOT / f"skin_Bone_state{i}.png" for i in range(3))


def hsv(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    value = rgb.max(axis=2)
    minimum = rgb.min(axis=2)
    delta = value - minimum
    saturation = np.divide(delta, value, out=np.zeros_like(value), where=value != 0)
    hue = np.zeros_like(value)
    nonzero = delta != 0
    red, green, blue = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    red_case = nonzero & (value == red)
    green_case = nonzero & (value == green) & ~red_case
    blue_case = nonzero & (value == blue) & ~red_case & ~green_case
    hue[red_case] = ((green[red_case] - blue[red_case]) / delta[red_case]) % 6
    hue[green_case] = (blue[green_case] - red[green_case]) / delta[green_case] + 2
    hue[blue_case] = (red[blue_case] - green[blue_case]) / delta[blue_case] + 4
    return hue * 60.0, saturation, value


def bone_ramp(luminance: np.ndarray) -> np.ndarray:
    """Warm ceramic ramp used only as a fallback for generator edge drift."""
    ramp = np.array(
        [
            [100, 93, 79],
            [185, 174, 152],  # requested shadow #B9AE98
            [220, 210, 190],
            [244, 234, 212],  # requested base #F4EAD4
            [255, 252, 241],
        ],
        dtype=np.uint8,
    )
    bands = np.select(
        [luminance < 0.20, luminance < 0.34, luminance < 0.52, luminance < 0.72],
        [0, 1, 2, 3],
        default=4,
    )
    return ramp[bands]


def armour_mask(reference: np.ndarray) -> np.ndarray:
    """Find the warm gold plate material while leaving red/orange damage untouched."""
    rgb = reference.astype(np.float32) / 255.0
    hue, saturation, value = hsv(rgb)
    red, green, blue = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    non_green = np.any(reference != GREEN, axis=2)

    # Main gold ramp and its dark warm-brown members. Orange/red damage starts below
    # hue 28 in the approved sheets, so it is excluded explicitly.
    warm_plate = (
        non_green
        & (hue >= 28.0)
        & (hue <= 72.0)
        & (saturation > 0.07)
        & (value > 0.045)
        & (red >= blue)
        & (green >= blue)
    )
    explicit_gold = non_green & (hue >= 35.0) & (hue <= 55.0) & (saturation > 0.50)
    dark_plate = (
        non_green
        & (red >= blue)
        & (green >= blue)
        & (red > blue + 0.008)
        & (green > blue + 0.004)
        & (value > 0.08)
        & (hue >= 22.0)
        & (hue <= 78.0)
    )
    mask = warm_plate | explicit_gold | dark_plate

    # Never repaint the approved bone-white hit flash or the red player marks.
    mask[1024:1536, 1024:1536] = False
    red_mark = ((hue < 20.0) | (hue > 345.0)) & (saturation > 0.30)
    mask &= ~red_mark
    return mask


def generated_bone(source: np.ndarray, luminance: np.ndarray) -> np.ndarray:
    """Quantise generator colour into the Bone ramp without importing its background."""
    src = source.astype(np.float32) / 255.0
    src_hue, src_sat, src_value = hsv(src)
    src_red, src_green, src_blue = src[..., 0], src[..., 1], src[..., 2]
    src_green_bg = (src_green > 0.72) & (src_red < 0.35) & (src_blue < 0.35)
    src_cyan = (src_blue > src_red * 1.15) & (src_green > src_red * 1.10) & (src_sat > 0.20)
    src_red_fx = ((src_hue < 20.0) | (src_hue > 345.0)) & (src_sat > 0.30)
    usable = ~src_green_bg & ~src_cyan & ~src_red_fx & (src_value > 0.18)

    # Preserve the generated ceramic's broad value structure, but snap it to a
    # limited warm ramp so the result remains pixel art rather than anti-aliased paint.
    source_luma = 0.299 * src_red + 0.587 * src_green + 0.114 * src_blue
    ramp = bone_ramp(np.clip(0.55 * luminance + 0.45 * source_luma, 0.0, 1.0))
    return ramp, usable


def shifted(mask: np.ndarray, dy: int, dx: int) -> np.ndarray:
    result = np.zeros_like(mask)
    source_y0 = max(0, -dy)
    source_y1 = mask.shape[0] - max(0, dy)
    source_x0 = max(0, -dx)
    source_x1 = mask.shape[1] - max(0, dx)
    target_y0 = max(0, dy)
    target_y1 = mask.shape[0] - max(0, -dy)
    target_x0 = max(0, dx)
    target_x1 = mask.shape[1] - max(0, -dx)
    result[target_y0:target_y1, target_x0:target_x1] = mask[source_y0:source_y1, source_x0:source_x1]
    return result


def dilated(mask: np.ndarray, radius: int) -> np.ndarray:
    result = np.zeros_like(mask)
    for dy in range(-radius, radius + 1):
        for dx in range(-radius, radius + 1):
            result |= shifted(mask, dy, dx)
    return result


def make_sheet(reference_path: Path, candidate_path: Path, output_path: Path) -> None:
    reference = np.asarray(Image.open(reference_path).convert("RGB"), dtype=np.uint8)
    candidate_small = np.asarray(Image.open(candidate_path).convert("RGB"), dtype=np.uint8)
    if reference.shape != (1536, 1536, 3):
        raise ValueError(f"{reference_path.name}: expected 1536x1536 RGB")
    if candidate_small.shape[:2] != (1254, 1254):
        raise ValueError(f"{candidate_path.name}: expected generated 1254x1254 preview")

    # Generator output is a 3x3 square preview. Normalize it to reference cells with
    # nearest-neighbour only; the reference remains the authoritative geometry.
    candidate = np.asarray(
        Image.fromarray(candidate_small, mode="RGB").resize((1536, 1536), Image.Resampling.NEAREST),
        dtype=np.uint8,
    )
    ref_rgb = reference.astype(np.float32) / 255.0
    ref_luma = 0.299 * ref_rgb[..., 0] + 0.587 * ref_rgb[..., 1] + 0.114 * ref_rgb[..., 2]
    repaint, usable = generated_bone(candidate, ref_luma)
    mask = armour_mask(reference)

    # The wreck state has very little gold left. The generated repaint is also used
    # as a visual segmentation guide: its warm-bone regions identify surviving
    # ceramic plate shadows and rims that the reference hue alone cannot separate
    # from soot/heat staining. A small nearest-neighbour fringe keeps the full
    # colourway legible without changing the silhouette or blue/red systems.
    candidate_rgb = candidate.astype(np.float32) / 255.0
    candidate_hue, candidate_sat, _ = hsv(candidate_rgb)
    candidate_red, candidate_green, candidate_blue = candidate_rgb[..., 0], candidate_rgb[..., 1], candidate_rgb[..., 2]
    candidate_bg = (candidate_green > 0.72) & (candidate_red < 0.35) & (candidate_blue < 0.35)
    candidate_cyan = (candidate_blue > candidate_red * 1.15) & (candidate_green > candidate_red * 1.10) & (candidate_sat > 0.20)
    candidate_red_fx = ((candidate_hue < 20.0) | (candidate_hue > 345.0)) & (candidate_sat > 0.30)
    candidate_bone = (
        ~candidate_bg
        & ~candidate_cyan
        & ~candidate_red_fx
        & (candidate_red > 0.47)
        & (candidate_red >= candidate_blue * 0.95)
        & (candidate_green >= candidate_blue * 0.90)
    )
    non_green = np.any(reference != GREEN, axis=2)
    reference_rgb = reference.astype(np.float32) / 255.0
    reference_hue, reference_sat, reference_value = hsv(reference_rgb)
    reference_blue = (
        (((reference_hue >= 150.0) & (reference_hue <= 270.0)) | (reference_hue >= 170.0) & (reference_hue <= 230.0))
        & (reference_sat > 0.16)
        & (reference_value > 0.08)
    )
    reference_red_fx = ((reference_hue < 20.0) | (reference_hue > 345.0)) & (reference_sat > 0.30)
    extended = dilated(candidate_bone & non_green, radius=8) & non_green
    extended &= ~candidate_cyan
    extended &= ~candidate_red_fx
    extended &= ~reference_blue
    extended &= ~reference_red_fx
    extended[1024:1536, 1024:1536] = False
    mask |= extended

    result = reference.copy()
    # Generated repaint supplies the new ceramic value/rim-light structure where it
    # aligns; the explicit ramp is used only where generator drift hits green or FX.
    result[mask] = bone_ramp(ref_luma)[mask]
    use_generated = mask & usable
    result[use_generated] = repaint[use_generated]

    # Keep the flat key and approved hit-flash cell exact.
    non_green = np.any(reference != GREEN, axis=2)
    result[~non_green] = GREEN
    result[1024:1536, 1024:1536] = reference[1024:1536, 1024:1536]
    Image.fromarray(result, mode="RGB").resize((3072, 3072), Image.Resampling.NEAREST).save(
        output_path, format="PNG", optimize=False
    )


def main() -> int:
    for reference, candidate, output in zip(REFERENCES, CANDIDATES, OUTPUTS):
        make_sheet(reference, candidate, output)
        print(f"wrote {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
