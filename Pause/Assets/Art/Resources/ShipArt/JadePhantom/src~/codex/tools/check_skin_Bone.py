"""Validate Bone JadePhantom sheets against the approved reference sheets.

Run from the repository root:
    python3 Pause/Assets/Art/Resources/ShipArt/JadePhantom/src~/codex/tools/check_skin_Bone.py

The 3072px output is reduced to the 1536px reference grid with nearest-neighbour
before each 512px cell is compared.
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
    h, s, _ = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
    return 0.52 <= h <= 0.78 and s >= 0.25 and b > g * 1.15 and b > r * 1.20


def metric(ref: Image.Image, got: Image.Image) -> tuple[float, float, float]:
    ref_px, got_px = ref.load(), got.load()
    silhouette_bad = canopy_bad = changed = total = canopy_total = 0
    for y in range(512):
        for x in range(512):
            a = ref_px[x, y]
            b = got_px[x, y]
            ref_solid = a != BG
            if (b != BG) != ref_solid:
                silhouette_bad += 1
            if is_blue(a):
                canopy_total += 1
                canopy_bad += a != b
            changed += a != b
            total += 1
    canopy_fraction = canopy_bad / canopy_total if canopy_total else 0.0
    return silhouette_bad / total, canopy_fraction, changed / total


def main() -> int:
    failures = 0
    for state in range(3):
        ref_sheet = Image.open(HERE / f"skinref_state{state}.png").convert("RGB")
        got_sheet = Image.open(HERE / f"skin_Bone_state{state}.png").convert("RGB")
        if ref_sheet.size != (1536, 1536):
            raise SystemExit(f"skinref_state{state}.png: expected 1536x1536, got {ref_sheet.size}")
        if got_sheet.size != (3072, 3072):
            raise SystemExit(f"skin_Bone_state{state}.png: expected 3072x3072, got {got_sheet.size}")
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
