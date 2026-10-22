"""Audit Sodium sheets against the approved JadePhantom references.

Run from this directory or pass no arguments:
    python3 tools/check_skin_Sodium.py

The final 3072px sheet is reduced with nearest-neighbour for comparison to the
1536px reference. Silhouette and canopy metrics are mismatch fractions; the
changed metric is the fraction of pixels whose RGB value differs.
"""

from __future__ import annotations

import colorsys
from pathlib import Path

from PIL import Image


HERE = Path(__file__).resolve().parents[1]
BG = (0, 255, 0)


def is_blue(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    if rgb == BG or max(rgb) < 35:
        return False
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    return 0.52 <= h <= 0.78 and s >= 0.25 and b > g * 1.15 and b > r * 1.20


def masks(ref: Image.Image, got: Image.Image):
    ref_px, got_px = ref.load(), got.load()
    for y in range(512):
        for x in range(512):
            a = ref_px[x, y]
            b = got_px[x, y]
            yield a, b, a != BG, is_blue(a)


def metric(ref: Image.Image, got: Image.Image) -> tuple[float, float, float]:
    silhouette_bad = canopy_bad = changed = total = canopy_total = 0
    for a, b, ref_solid, ref_blue in masks(ref, got):
        total += 1
        if (b != BG) != ref_solid:
            silhouette_bad += 1
        if ref_blue:
            canopy_total += 1
            canopy_bad += a != b
        changed += a != b
    canopy_fraction = canopy_bad / canopy_total if canopy_total else 0.0
    return silhouette_bad / total, canopy_fraction, changed / total


def main() -> int:
    failures = 0
    for state in range(3):
        ref_sheet = Image.open(HERE / f"skinref_state{state}.png").convert("RGB")
        got_sheet = Image.open(HERE / f"skin_Sodium_state{state}.png").convert("RGB")
        if got_sheet.size != (3072, 3072):
            raise SystemExit(f"skin_Sodium_state{state}.png: expected 3072x3072, got {got_sheet.size}")
        got_sheet = got_sheet.resize((1536, 1536), Image.Resampling.NEAREST)
        print(f"state{state}")
        for row in range(3):
            for col in range(3):
                box = (col * 512, row * 512, (col + 1) * 512, (row + 1) * 512)
                sil, canopy, changed = metric(ref_sheet.crop(box), got_sheet.crop(box))
                ok = sil < 0.015 and canopy < 0.06 and changed > 0.12
                failures += not ok
                print(
                    f"  cell {row * 3 + col}: silhouette={sil * 100:6.3f}% "
                    f"canopy={canopy * 100:6.3f}% changed={changed * 100:6.3f}% "
                    f"{'PASS' if ok else 'FAIL'}"
                )
    print("summary:", "PASS" if not failures else f"FAIL ({failures} cells)")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
