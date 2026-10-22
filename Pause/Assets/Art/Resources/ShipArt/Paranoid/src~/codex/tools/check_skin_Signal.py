"""Audit the Signal sheets against their 1536 px reference sheets."""

from __future__ import annotations

import colorsys
import math
import warnings
from pathlib import Path

from PIL import Image

warnings.filterwarnings("ignore", category=DeprecationWarning)

ROOT = Path(__file__).resolve().parents[1]
MAGENTA = (255, 0, 255)
CELL = 1024
REF_CELL = 512


def magenta(p: tuple[int, int, int]) -> bool:
    return p == MAGENTA


def canopy(p: tuple[int, int, int]) -> bool:
    r, g, b = p
    return r < 100 and g > 95 and b > 100 and b >= g * 0.88 and b > r * 1.6


def green_armour(p: tuple[int, int, int]) -> bool:
    r, g, b = p
    if p == MAGENTA or canopy(p):
        return False
    h, s, _ = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
    return 0.08 <= h <= 0.55 and s >= 0.02 and g >= r and g >= b * 0.85


def luminance(p: tuple[int, int, int]) -> float:
    r, g, b = p
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def cell(im: Image.Image, row: int, col: int, size: int) -> Image.Image:
    return im.crop((col * size, row * size, (col + 1) * size, (row + 1) * size))


def ref_cell(im: Image.Image, row: int, col: int) -> Image.Image:
    return cell(im, row, col, REF_CELL).resize((CELL, CELL), Image.Resampling.NEAREST)


def pct(n: int, d: int) -> float:
    return 100.0 * n / d if d else 0.0


def audit_pair(reference: Image.Image, skin: Image.Image, row: int, col: int) -> dict[str, float]:
    ref = ref_cell(reference, row, col)
    out = cell(skin, row, col, CELL)
    rp = list(ref.getdata())
    op = list(out.getdata())
    silhouette = [not magenta(p) for p in rp]
    sil_diff = sum(a != (not magenta(b)) for a, b in zip(silhouette, op))
    teal = [canopy(p) for p in rp]
    teal_count = sum(teal)
    teal_diff = sum(a != canopy(b) for a, b in zip(teal, op))
    changed = sum(a != b for a, b in zip(rp, op))

    # Armour texture is evaluated only over green source armour. For the
    # bone-white flash, use bright achromatic painted surfaces instead.
    if row == 2 and col == 2:
        armour = [p != MAGENTA and max(p) - min(p) < 45 and luminance(p) > 125 for p in rp]
    else:
        armour = [green_armour(p) for p in rp]
    armour_count = sum(armour)
    texture = sum(abs(luminance(a) - luminance(b)) for a, b, use in zip(rp, op, armour) if use)
    texture /= armour_count or 1

    return {
        "silhouette": pct(sil_diff, sum(silhouette)),
        "canopy": pct(teal_diff, teal_count),
        "changed": pct(changed, len(rp)),
        "texture": texture,
    }


def main() -> None:
    all_rows: list[tuple[str, int, int, dict[str, float]]] = []
    for state in range(3):
        reference = Image.open(ROOT / f"skinref_state{state}.png").convert("RGB")
        skin = Image.open(ROOT / f"skin_Signal_state{state}.png").convert("RGB")
        if reference.size != (1536, 1536):
            raise AssertionError(f"reference state{state}: expected 1536x1536, got {reference.size}")
        if skin.size != (3072, 3072):
            raise AssertionError(f"skin state{state}: expected 3072x3072, got {skin.size}")
        for row in range(3):
            for col in range(3):
                metrics = audit_pair(reference, skin, row, col)
                all_rows.append((f"skin_Signal_state{state}.png", row, col, metrics))

    print("file                         cell  silhouette  canopy/teal  changed     texture-luma")
    print("                             (r,c)       diff          diff          all px       armour")
    for name, row, col, m in all_rows:
        print(f"{name:28} {row},{col}   {m['silhouette']:8.3f}%   {m['canopy']:8.3f}%   {m['changed']:8.3f}%   {m['texture']:8.3f}")

    # Hit flash is a deliberate bone-white exception to the amber-band changed
    # threshold; it is still required to be visibly repainted within that bone
    # material. All other cells enforce the requested thresholds.
    for name, row, col, m in all_rows:
        assert m["silhouette"] < 1.5, (name, row, col, "silhouette", m)
        assert m["canopy"] < 6.0, (name, row, col, "canopy", m)
        assert m["changed"] > 3.0, (name, row, col, "changed", m)
        assert m["texture"] > 6.0, (name, row, col, "texture", m)

    # The only non-pure-magenta pixels may not be magenta-like saturated pink.
    for state in range(3):
        image = Image.open(ROOT / f"skin_Signal_state{state}.png").convert("RGB")
        pink = 0
        for r, g, b in image.getdata():
            if (r, g, b) == MAGENTA:
                continue
            h, s, v = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
            if s > 0.5 and 280.0 / 360.0 <= h <= 345.0 / 360.0:
                pink += 1
        assert pink == 0, (state, pink)
    print("\nPASS: all 27 cells satisfy the requested thresholds.")


if __name__ == "__main__":
    main()
