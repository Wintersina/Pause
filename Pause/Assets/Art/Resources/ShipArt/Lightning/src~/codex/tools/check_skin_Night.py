#!/usr/bin/env python3
"""Audit the Night flipbook against the approved 3x3 reference sheets."""

from __future__ import annotations

import colorsys
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
CELL_REF = 512
CELL_OUT = 1024
GREEN = (0, 255, 0)
REFS = [ROOT / f"skinref_state{i}.png" for i in range(3)]
OUTS = [ROOT / f"skin_Night_state{i}.png" for i in range(3)]


def is_green(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    return g >= 235 and r <= 25 and b <= 25


def luma(rgb: tuple[int, int, int]) -> float:
    r, g, b = rgb
    return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0


def blue_region(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    # Canopy/blue-region proxy from the reference: saturated cyan/blue pixels, excluding green.
    return not is_green(rgb) and s > 0.18 and (0.47 < h < 0.72) and v > 0.08


def cell(im: Image.Image, col: int, row: int, size: int) -> Image.Image:
    return im.crop((col * size, row * size, (col + 1) * size, (row + 1) * size))


def up2(im: Image.Image) -> Image.Image:
    return im.resize((im.width * 2, im.height * 2), Image.Resampling.NEAREST)


def audit(ref: Image.Image, out: Image.Image) -> tuple[float, float, float]:
    ref = ref.convert("RGB")
    out = out.convert("RGB")
    if out.size != (3072, 3072):
        raise ValueError(f"Night output must be 3072x3072, got {out.size}")
    silhouette_total = 0
    silhouette_diff = 0
    blue_total = 0
    blue_diff = 0
    changed = 0
    total = ref.width * ref.height
    rp = ref.load()
    op = out.resize(ref.size, Image.Resampling.NEAREST).load()
    for y in range(ref.height):
        for x in range(ref.width):
            rr = rp[x, y]
            oo = op[x, y]
            rmask = not is_green(rr)
            omask = not is_green(oo)
            silhouette_total += rmask
            silhouette_diff += rmask != omask
            blue = blue_region(rr)
            blue_total += blue
            blue_diff += blue and rr != oo
            changed += rr != oo
    silhouette_pct = 100 * silhouette_diff / max(1, silhouette_total)
    blue_pct = 100 * blue_diff / max(1, blue_total)
    changed_pct = 100 * changed / total
    return silhouette_pct, blue_pct, changed_pct


def main() -> int:
    grand = []
    for state, (ref_path, out_path) in enumerate(zip(REFS, OUTS)):
        ref = Image.open(ref_path)
        out = Image.open(out_path)
        state_values = []
        for row in range(3):
            for col in range(3):
                ref_cell = cell(ref, col, row, CELL_REF)
                out_cell = cell(out, col, row, CELL_OUT).resize((CELL_REF, CELL_REF), Image.Resampling.NEAREST)
                values = audit(ref_cell, out_cell.resize((3072, 3072), Image.Resampling.NEAREST))
                state_values.append(values)
                print(
                    f"state{state} cell({row},{col}): "
                    f"silhouette={values[0]:.3f}% "
                    f"canopy_blue={values[1]:.3f}% "
                    f"changed={values[2]:.3f}%"
                )
        grand.extend(state_values)
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
