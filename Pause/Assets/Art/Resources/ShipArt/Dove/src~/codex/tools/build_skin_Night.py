#!/usr/bin/env python3
"""Build the Night sheets from generated repaint candidates and locked references.

The generated candidate supplies the new armour ramp.  The approved reference supplies
the exact cell geometry and every non-armour pixel, so AI repaint drift cannot move a pose.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
TARGETS = [
    (ROOT / "skinref_state0.png", Path("/Users/sina/.codex/generated_images/01a16486-92ce-79d1-8489-b06f50723685/exec-037761b3-b364-4f44-a984-f85492b582ba.png"), ROOT / "skin_Night_state0.png"),
    (ROOT / "skinref_state1.png", Path("/Users/sina/.codex/generated_images/01a16486-92ce-79d1-8489-b06f50723685/exec-e399e81d-18d6-4b13-a6b0-0a20791c4950.png"), ROOT / "skin_Night_state1.png"),
    (ROOT / "skinref_state2.png", Path("/Users/sina/.codex/generated_images/01a16486-92ce-79d1-8489-b06f50723685/exec-da3b864d-ee4e-4e67-bcbe-051f1ce0a884.png"), ROOT / "skin_Night_state2.png"),
]

GREEN = np.array([0, 255, 0], dtype=np.uint8)
SHADOW = np.array([0x1A, 0x1F, 0x45], dtype=np.float32)
BASE = np.array([0x2A, 0x2E, 0x6B], dtype=np.float32)
MID = np.array([0x3F, 0x4B, 0x8A], dtype=np.float32)
RIM = np.array([0x6E, 0xF2, 0xEE], dtype=np.float32)
PALE = np.array([0xD8, 0xFC, 0xFF], dtype=np.float32)
BONE_SHADOW = np.array([0x8F, 0x88, 0x7F], dtype=np.float32)
BONE_BASE = np.array([0xD6, 0xCC, 0xBA], dtype=np.float32)
BONE_LIGHT = np.array([0xFF, 0xF4, 0xD8], dtype=np.float32)


def lerp(a: np.ndarray, b: np.ndarray, t: np.ndarray) -> np.ndarray:
    return a[None, :] * (1.0 - t[:, None]) + b[None, :] * t[:, None]


def candidate_to_night(candidate: np.ndarray, fallback: np.ndarray) -> np.ndarray:
    """Turn a generated blue/cyan repaint pixel into the requested Night ramp."""
    cand = candidate.astype(np.float32)
    valid = ~(
        (cand[:, 0] < 8)
        & (cand[:, 1] > 235)
        & (cand[:, 2] < 8)
    )
    # Use the candidate's painted lightness where available.  Reference lightness is
    # the stable fallback when the candidate's small resampling drift hits green.
    lum_c = cand @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    lum_f = fallback.astype(np.float32) @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    lum = np.where(valid, lum_c, lum_f)
    # The candidate is only used as a ramp/lightness guide.  The explicit palette keeps
    # the colourway in indigo-navy with cyan edge lighting and no accidental magenta.
    out = np.empty_like(cand)
    t = np.clip((lum - 20.0) / 80.0, 0.0, 1.0)
    low = lum < 100.0
    out[low] = lerp(SHADOW, BASE, t[low])
    t2 = np.clip((lum[~low] - 100.0) / 70.0, 0.0, 1.0)
    out[~low] = lerp(BASE, MID, t2)
    hi = lum >= 170.0
    t3 = np.clip((lum[hi] - 170.0) / 75.0, 0.0, 1.0)
    out[hi] = lerp(MID, RIM, t3)
    very_hi = lum >= 235.0
    t4 = np.clip((lum[very_hi] - 235.0) / 20.0, 0.0, 1.0)
    out[very_hi] = lerp(RIM, PALE, t4)
    return np.rint(out).clip(0, 255).astype(np.uint8)


def candidate_to_bone(candidate: np.ndarray, fallback: np.ndarray) -> np.ndarray:
    """Keep the hit-flash bone-white while refreshing its darker painted ramp."""
    cand = candidate.astype(np.float32)
    lum = cand @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    fallback_lum = fallback.astype(np.float32) @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    lum = np.where((lum < 8) | ((cand[:, 1] > 235) & (cand[:, 2] < 8)), fallback_lum, lum)
    out = np.empty_like(cand)
    t = np.clip((lum - 30.0) / 160.0, 0.0, 1.0)
    out[:] = lerp(BONE_SHADOW, BONE_BASE, t)
    hi = lum >= 190.0
    out[hi] = lerp(BONE_BASE, BONE_LIGHT, np.clip((lum[hi] - 190.0) / 65.0, 0.0, 1.0))
    return np.rint(out).clip(0, 255).astype(np.uint8)


def armor_mask(ref: np.ndarray, cell: int) -> np.ndarray:
    """Select teal armour only; exclude glass, lights, tail, damage, and hit flash."""
    h, w, _ = ref.shape
    y0, y1 = divmod(cell, 3)[0] * (h // 3), (divmod(cell, 3)[0] + 1) * (h // 3)
    x0, x1 = divmod(cell, 3)[1] * (w // 3), (divmod(cell, 3)[1] + 1) * (w // 3)
    p = ref[y0:y1, x0:x1].astype(np.int16)
    r, g, b = p[..., 0], p[..., 1], p[..., 2]
    not_green = np.any(p != GREEN.astype(np.int16), axis=-1)
    # Canopy glass is blue-dominant; armour is the teal family with green and blue
    # comparable to red.  The relaxed ratio catches the dark plate shading in banked
    # and wreck poses; bright whites and red/orange damage remain untouched.
    canopy = (b > g * 1.18) & (b > r * 1.25) & (g > 20)
    teal = (g > r * 0.55) & (b > r * 0.55) & (g > 3) & (b > 3)
    bright_white = (r > 150) & (g > 150) & (b > 150)
    red_orange = (r > g * 1.15) & (r > b * 1.15)
    armour = not_green & teal & ~canopy & ~bright_white & ~red_orange
    return armour


def bone_flash_mask(ref: np.ndarray) -> np.ndarray:
    p = ref.astype(np.int16)
    r, g, b = p[..., 0], p[..., 1], p[..., 2]
    non_green = np.any(p != GREEN.astype(np.int16), axis=-1)
    spread = np.maximum.reduce([r, g, b]) - np.minimum.reduce([r, g, b])
    return non_green & (r > 45) & (g > 40) & (b > 30) & (spread < 65)


def build(ref_path: Path, candidate_path: Path, output_path: Path) -> int:
    ref_im = Image.open(ref_path).convert("RGB")
    cand_im = Image.open(candidate_path).convert("RGB")
    ref = np.asarray(ref_im)
    cand = np.asarray(cand_im.resize(ref_im.size, Image.Resampling.NEAREST))
    base = ref.copy()
    changed = 0
    for cell in range(9):
        row, col = divmod(cell, 3)
        y0, y1 = row * 512, (row + 1) * 512
        x0, x1 = col * 512, (col + 1) * 512
        mask = armor_mask(ref, cell)
        if cell == 8:
            mask = bone_flash_mask(ref[y0:y1, x0:x1])
            pixels = candidate_to_bone(cand[y0:y1, x0:x1][mask], ref[y0:y1, x0:x1][mask])
        else:
            pixels = candidate_to_night(cand[y0:y1, x0:x1][mask], ref[y0:y1, x0:x1][mask])
        base[y0:y1, x0:x1][mask] = pixels
        changed += int(mask.sum())
    out = Image.fromarray(base, "RGB").resize((3072, 3072), Image.Resampling.NEAREST)
    out.save(output_path, format="PNG", optimize=True)
    return changed


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--force", action="store_true", help="allow replacing this helper's generated outputs")
    args = parser.parse_args()
    for ref, candidate, output in TARGETS:
        if output.exists() and not args.force:
            raise SystemExit(f"refusing to overwrite existing file: {output}")
        if not candidate.exists():
            raise SystemExit(f"missing generated candidate: {candidate}")
        count = build(ref, candidate, output)
        print(f"{output.name}: recoloured armour pixels at native resolution = {count}")


if __name__ == "__main__":
    main()
