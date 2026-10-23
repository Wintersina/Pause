#!/usr/bin/env python3
"""Audit the Bone flipbook against the approved 3x3 reference sheets."""

from __future__ import annotations

import colorsys
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
CELL_REF = 512
CELL_OUT = 1024
GREEN = (0, 255, 0)
REFS = [ROOT / f"skinref_state{i}.png" for i in range(3)]
OUTS = [ROOT / f"skin_Bone_state{i}.png" for i in range(3)]


def is_green(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    return g >= 235 and r <= 25 and b <= 25


def blue_region(rgb: tuple[int, int, int]) -> bool:
    if is_green(rgb):
        return False
    r, g, b = rgb
    h, s, v = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
    return s > 0.18 and 0.47 < h < 0.72 and v > 0.08


def cell(im: Image.Image, col: int, row: int, size: int) -> Image.Image:
    return im.crop((col * size, row * size, (col + 1) * size, (row + 1) * size))


def audit(ref: Image.Image, out: Image.Image) -> tuple[float, float, float]:
    ref = ref.convert("RGB")
    out = out.convert("RGB")
    if out.size != (CELL_REF, CELL_REF):
        raise ValueError(f"audit cell must be {CELL_REF}x{CELL_REF}, got {out.size}")
    rp = ref.load()
    op = out.load()
    silhouette_total = silhouette_diff = 0
    blue_total = blue_diff = changed = 0
    for y in range(CELL_REF):
        for x in range(CELL_REF):
            rr = rp[x, y]
            oo = op[x, y]
            ref_mask = not is_green(rr)
            out_mask = not is_green(oo)
            silhouette_total += ref_mask
            silhouette_diff += ref_mask != out_mask
            is_blue = blue_region(rr)
            blue_total += is_blue
            blue_diff += is_blue and rr != oo
            changed += rr != oo
    return (
        100.0 * silhouette_diff / max(1, silhouette_total),
        100.0 * blue_diff / max(1, blue_total),
        100.0 * changed / (CELL_REF * CELL_REF),
    )


def main() -> int:
    grand: list[tuple[float, float, float]] = []
    for state, (ref_path, out_path) in enumerate(zip(REFS, OUTS)):
        ref = Image.open(ref_path).convert("RGB")
        out = Image.open(out_path).convert("RGB")
        if ref.size != (1536, 1536):
            raise ValueError(f"{ref_path.name}: expected 1536x1536, got {ref.size}")
        if out.size != (3072, 3072):
            raise ValueError(f"{out_path.name}: expected 3072x3072, got {out.size}")
        for row in range(3):
            for col in range(3):
                ref_cell = cell(ref, col, row, CELL_REF)
                out_cell = cell(out, col, row, CELL_OUT).resize((CELL_REF, CELL_REF), Image.Resampling.NEAREST)
                values = audit(ref_cell, out_cell)
                grand.append(values)
                print(
                    f"state{state} cell({row},{col}): "
                    f"silhouette={values[0]:.3f}% "
                    f"canopy_blue={values[1]:.3f}% "
                    f"changed={values[2]:.3f}%"
                )
    worst_silhouette = max(v[0] for v in grand)
    worst_blue = max(v[1] for v in grand)
    least_changed = min(v[2] for v in grand)
    print(
        f"SUMMARY worst_silhouette={worst_silhouette:.3f}% "
        f"worst_canopy_blue={worst_blue:.3f}% "
        f"least_changed={least_changed:.3f}%"
    )
    if worst_silhouette >= 1.5 or worst_blue >= 6.0 or least_changed <= 12.0:
        raise SystemExit(1)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
