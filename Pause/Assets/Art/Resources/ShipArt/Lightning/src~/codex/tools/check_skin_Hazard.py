#!/usr/bin/env python3
"""Audit the Hazard flipbook against the approved 3x3 references."""

from __future__ import annotations

import colorsys
import math
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
CELL_REF = 512
CELL_OUT = 1024
GREEN = (0, 255, 0)
REFS = [ROOT / f"skinref_state{i}.png" for i in range(3)]
OUTS = [ROOT / f"skin_Hazard_state{i}.png" for i in range(3)]


def is_green(rgb: tuple[int, int, int]) -> bool:
    return rgb == GREEN


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
    out = out.convert("RGB").resize(ref.size, Image.Resampling.NEAREST)
    rp = ref.load()
    op = out.load()
    silhouette_total = silhouette_diff = 0
    blue_total = blue_diff = 0
    changed = 0
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
    return (
        100.0 * silhouette_diff / max(1, silhouette_total),
        100.0 * blue_diff / max(1, blue_total),
        100.0 * changed / (ref.width * ref.height),
    )


def band_audit(out: Image.Image) -> tuple[float, float]:
    key = cell(out.convert("RGB"), 0, 0, CELL_OUT)
    px = key.load()
    silhouette = 0
    dark_band = []
    for y in range(CELL_OUT):
        for x in range(CELL_OUT):
            rgb = px[x, y]
            if is_green(rgb):
                continue
            silhouette += 1
            # A 14-level Euclidean RGB tolerance recognizes the requested band ramp
            # while excluding unrelated pre-existing gunmetal panels elsewhere on ship.
            if min(sum((rgb[i] - c[i]) ** 2 for i in range(3)) for c in ((44, 45, 64), (26, 26, 40))) <= 14**2:
                dark_band.append((x, y))
    if not dark_band:
        return 0.0, 0.0
    norm = math.sqrt(2.0)
    within = 0
    for x, y in dark_band:
        perpendicular = abs((x + y) - 1060.0) / norm
        within += perpendicular <= 70.0
    return 100.0 * len(dark_band) / max(1, silhouette), 100.0 * within / len(dark_band)


def main() -> int:
    grand = []
    for state, (ref_path, out_path) in enumerate(zip(REFS, OUTS)):
        ref = Image.open(ref_path)
        out = Image.open(out_path)
        if out.size != (3072, 3072):
            raise ValueError(f"{out_path.name}: expected 3072x3072, got {out.size}")
        for row in range(3):
            for col in range(3):
                values = audit(cell(ref, col, row, CELL_REF), cell(out, col, row, CELL_OUT))
                grand.append(values)
                print(
                    f"state{state} cell({row},{col}): "
                    f"silhouette={values[0]:.3f}% "
                    f"canopy_blue={values[1]:.3f}% "
                    f"changed={values[2]:.3f}%"
                )
    band_cover, band_alignment = band_audit(Image.open(OUTS[0]))
    worst_silhouette = max(v[0] for v in grand)
    worst_blue = max(v[1] for v in grand)
    least_changed = min(v[2] for v in grand)
    print(
        f"SUMMARY worst_silhouette={worst_silhouette:.3f}% "
        f"worst_canopy_blue={worst_blue:.3f}% "
        f"least_changed={least_changed:.3f}% "
        f"band_coverage={band_cover:.3f}% "
        f"band_within_70px={band_alignment:.3f}%"
    )
    failed = (
        worst_silhouette >= 1.5
        or worst_blue >= 6.0
        or least_changed <= 3.0
        or not (6.0 <= band_cover <= 20.0)
        or band_alignment < 90.0
    )
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
