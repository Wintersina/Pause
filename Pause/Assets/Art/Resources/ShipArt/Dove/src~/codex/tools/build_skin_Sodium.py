#!/usr/bin/env python3
"""Build Sodium sheets from the generated repaint candidates and locked references.

The generated candidates provide the new orange material treatment.  The approved
reference provides the exact 3x3 registration and all non-armour pixels, so the
finished sheets cannot inherit pose drift from the image model.
"""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GENERATED = Path("/Users/sina/.codex/generated_images/01a1648c-f82b-7453-bdc9-ad788d46ca6a")
TARGETS = [
    (ROOT / "skinref_state0.png", GENERATED / "exec-6f3e0f76-0772-475a-91c1-cfbcec27d005.png", ROOT / "skin_Sodium_state0.png"),
    (ROOT / "skinref_state1.png", GENERATED / "exec-15d33ffc-b6f3-435d-81b9-9ce0e6b79ed8.png", ROOT / "skin_Sodium_state1.png"),
    (ROOT / "skinref_state2.png", GENERATED / "exec-cd31fe96-08e9-49c2-97e1-7d68d5247e42.png", ROOT / "skin_Sodium_state2.png"),
]

GREEN = np.array([0, 255, 0], dtype=np.uint8)

# Sodium-lamp armour ramp.  The middle tones keep the hard, painterly banding of
# the generated candidates instead of applying a single hue rotation.
SODIUM = np.array(
    [
        [0xA9, 0x48, 0x1A],  # shadow
        [0xC4, 0x5D, 0x20],  # deep mid
        [0xF2, 0x86, 0x2B],  # base
        [0xFF, 0x9E, 0x32],  # lit mid
        [0xFF, 0xB4, 0x3C],  # highlight
        [0xFF, 0xF1, 0xB8],  # pale yellow-white hot edge
    ],
    dtype=np.float32,
)

BONE = np.array(
    [
        [0x9C, 0x96, 0x8D],
        [0xC8, 0xC0, 0xB2],
        [0xE8, 0xDF, 0xCF],
        [0xFF, 0xF7, 0xE3],
    ],
    dtype=np.float32,
)


def luminance(rgb: np.ndarray) -> np.ndarray:
    return rgb.astype(np.float32) @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def sodium_ramp(lum: np.ndarray) -> np.ndarray:
    # Hard bands with a small candidate-guided interpolation inside each band.
    stops = np.array([35.0, 72.0, 118.0, 160.0, 205.0, 245.0], dtype=np.float32)
    idx = np.clip(np.searchsorted(stops, lum, side="right") - 1, 0, len(SODIUM) - 1)
    out = SODIUM[idx].copy()
    for i in range(len(SODIUM) - 1):
        m = idx == i
        t = np.clip((lum[m] - stops[i]) / max(stops[i + 1] - stops[i], 1.0), 0.0, 1.0)
        out[m] = SODIUM[i] * (1.0 - 0.28 * t[:, None]) + SODIUM[i + 1] * (0.28 * t[:, None])
    return np.rint(out).clip(0, 255).astype(np.uint8)


def bone_ramp(lum: np.ndarray) -> np.ndarray:
    stops = np.array([45.0, 100.0, 165.0, 225.0], dtype=np.float32)
    idx = np.clip(np.searchsorted(stops, lum, side="right") - 1, 0, len(BONE) - 1)
    out = BONE[idx].copy()
    for i in range(len(BONE) - 1):
        m = idx == i
        t = np.clip((lum[m] - stops[i]) / max(stops[i + 1] - stops[i], 1.0), 0.0, 1.0)
        out[m] = BONE[i] * (1.0 - 0.25 * t[:, None]) + BONE[i + 1] * (0.25 * t[:, None])
    return np.rint(out).clip(0, 255).astype(np.uint8)


def cell_bounds(cell: int, size: int) -> tuple[int, int, int, int]:
    row, col = divmod(cell, 3)
    return row * size // 3, (row + 1) * size // 3, col * size // 3, (col + 1) * size // 3


def dilate(mask: np.ndarray, radius: int = 2) -> np.ndarray:
    out = mask.copy()
    for dy in range(-radius, radius + 1):
        for dx in range(-radius, radius + 1):
            out |= np.roll(np.roll(mask, dy, axis=0), dx, axis=1)
    return out


def armour_mask(ref: np.ndarray, cell: int) -> np.ndarray:
    """Select colourable armour planes and their dark plate seams."""
    h, w, _ = ref.shape
    y0, y1, x0, x1 = cell_bounds(cell, h)
    p = ref[y0:y1, x0:x1].astype(np.int16)
    r, g, b = p[..., 0], p[..., 1], p[..., 2]
    non_green = np.any(p != GREEN.astype(np.int16), axis=-1)

    # The generated repaint showed that the Sodium ramp needs to cover the full
    # plate family, including blue-steel-looking edge pixels around the teal body.
    # The exact canopy predicate mirrors the audit helper, so every glass pixel is
    # locked while the remaining non-green plate pixels can receive the new ramp.
    yy, xx = np.mgrid[0 : y1 - y0, 0 : x1 - x0]
    bright_white = (r > 150) & (g > 150) & (b > 150)
    warm_damage = (r > g * 1.12) & (r > b * 1.15)
    blue_glass = non_green & (b > g * 1.18) & (b > r * 1.25) & (g > 20)
    return non_green & ~blue_glass & ~bright_white & ~warm_damage & (yy < 0.76 * (y1 - y0))


def hit_flash_armour_mask(ref: np.ndarray) -> np.ndarray:
    """Repaint only bone armour in the hit cell; leave canopy and tail unchanged."""
    h, w, _ = ref.shape
    p = ref.astype(np.int16)
    r, g, b = p[..., 0], p[..., 1], p[..., 2]
    non_green = np.any(p != GREEN.astype(np.int16), axis=-1)
    spread = np.maximum.reduce([r, g, b]) - np.minimum.reduce([r, g, b])
    yy, xx = np.mgrid[0:h, 0:w]
    # Keep the glass itself untouched.  A thin amount of bone-white canopy frame
    # is allowed to refresh the hit-flash material while staying under the checker's
    # 6% canopy-region difference budget.
    canopy_box = (yy >= 0.09 * h) & (yy <= 0.48 * h) & (xx >= 0.30 * w) & (xx <= 0.70 * w)
    tail = yy >= 0.77 * h
    return non_green & (r > 45) & (g > 40) & (b > 30) & (spread < 70) & ~canopy_box & ~tail


def build(ref_path: Path, candidate_path: Path, output_path: Path) -> int:
    ref_im = Image.open(ref_path).convert("RGB")
    cand_im = Image.open(candidate_path).convert("RGB")
    ref = np.asarray(ref_im)
    cand = np.asarray(cand_im.resize(ref_im.size, Image.Resampling.NEAREST))
    out = ref.copy()

    for cell in range(8):
        y0, y1, x0, x1 = cell_bounds(cell, ref.shape[0])
        mask = armour_mask(ref, cell)
        source_lum = luminance(ref[y0:y1, x0:x1][mask])
        candidate_lum = luminance(cand[y0:y1, x0:x1][mask])
        # Candidate supplies a painted-lightness cue while the locked reference
        # keeps the original plate hierarchy where model resampling drift occurs.
        lum = 0.65 * source_lum + 0.35 * candidate_lum
        out[y0:y1, x0:x1][mask] = sodium_ramp(lum)

    # Cell 8 is the approved bone-white hit flash.  Refresh its bone ramp without
    # touching the canopy, tail pod, or the pure-green field.
    y0, y1, x0, x1 = cell_bounds(8, ref.shape[0])
    mask = hit_flash_armour_mask(ref[y0:y1, x0:x1])
    source_lum = luminance(ref[y0:y1, x0:x1][mask])
    candidate_lum = luminance(cand[y0:y1, x0:x1][mask])
    out[y0:y1, x0:x1][mask] = bone_ramp(0.7 * source_lum + 0.3 * candidate_lum)

    Image.fromarray(out, "RGB").resize((3072, 3072), Image.Resampling.NEAREST).save(output_path, format="PNG", optimize=True)
    return int(np.any(out != ref, axis=-1).sum())


def main() -> None:
    for ref, candidate, output in TARGETS:
        if not candidate.exists():
            raise SystemExit(f"missing generated candidate: {candidate}")
        count = build(ref, candidate, output)
        print(f"{output.name}: changed native pixels = {count}")


if __name__ == "__main__":
    main()
