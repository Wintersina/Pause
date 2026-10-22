"""Validate the Sodium repaint against the approved 3x3 reference sheets."""

from __future__ import annotations

import colorsys
import warnings
from pathlib import Path

from PIL import Image

warnings.filterwarnings("ignore", category=DeprecationWarning)

ROOT = Path(__file__).resolve().parents[1]
MAGENTA = (255, 0, 255)
CELL = 1024
REF_CELL = 512


def canopy(pixel: tuple[int, int, int]) -> bool:
    r, g, b = pixel
    return r < 100 and g > 95 and b > 100 and b >= g * 0.88 and b > r * 1.6


def green_armour(pixel: tuple[int, int, int]) -> bool:
    if pixel == MAGENTA or canopy(pixel):
        return False
    r, g, b = pixel
    h, s, _ = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
    return 0.105 <= h <= 0.50 and s >= 0.16 and g >= r * 0.92 and g >= b * 0.88 and g >= 22


def bone_armour(pixel: tuple[int, int, int]) -> bool:
    r, g, b = pixel
    return pixel != MAGENTA and max(pixel) - min(pixel) <= 58 and r >= 128 and g >= 122 and b >= 108


def luminance(pixel: tuple[int, int, int]) -> float:
    r, g, b = pixel
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def cell(image: Image.Image, row: int, col: int, size: int) -> Image.Image:
    return image.crop((col * size, row * size, (col + 1) * size, (row + 1) * size))


def ref_cell(image: Image.Image, row: int, col: int) -> Image.Image:
    return cell(image, row, col, REF_CELL).resize((CELL, CELL), Image.Resampling.NEAREST)


def pct(n: int, d: int) -> float:
    return 100.0 * n / d if d else 0.0


def audit_pair(reference: Image.Image, skin: Image.Image, row: int, col: int) -> dict[str, float]:
    ref = ref_cell(reference, row, col)
    out = cell(skin, row, col, CELL)
    rp = list(ref.getdata())
    op = list(out.getdata())

    ref_silhouette = [p != MAGENTA for p in rp]
    out_silhouette = [p != MAGENTA for p in op]
    silhouette_diff = sum(a != b for a, b in zip(ref_silhouette, out_silhouette))

    teal_pixels = [canopy(p) for p in rp]
    teal_count = sum(teal_pixels)
    canopy_diff = sum(a and x != y for a, x, y in zip(teal_pixels, rp, op))

    changed = sum(a != b for a, b in zip(rp, op))

    armour_pixels = [bone_armour(p) if row == 2 and col == 2 else green_armour(p) for p in rp]
    armour_count = sum(armour_pixels)
    texture = sum(abs(luminance(a) - luminance(b)) for a, b, use in zip(rp, op, armour_pixels) if use)
    texture /= armour_count or 1

    return {
        "silhouette": pct(silhouette_diff, sum(ref_silhouette)),
        "canopy": pct(canopy_diff, teal_count),
        "changed": pct(changed, len(rp)),
        "texture": texture,
    }


def assert_flat_background_and_no_pink() -> None:
    for state in range(3):
        image = Image.open(ROOT / f"skin_Sodium_state{state}.png").convert("RGB")
        for pixel in image.getdata():
            if pixel == MAGENTA:
                continue
            h, s, v = colorsys.rgb_to_hsv(*(channel / 255.0 for channel in pixel))
            if s > 0.5 and v > 0.25 and 280.0 / 360.0 <= h <= 345.0 / 360.0:
                raise AssertionError((state, "pink/magenta pixel", pixel))


def main() -> None:
    rows: list[tuple[str, int, int, dict[str, float]]] = []
    for state in range(3):
        reference = Image.open(ROOT / f"skinref_state{state}.png").convert("RGB")
        skin = Image.open(ROOT / f"skin_Sodium_state{state}.png").convert("RGB")
        if reference.size != (1536, 1536):
            raise AssertionError(f"reference state{state}: expected 1536x1536, got {reference.size}")
        if skin.size != (3072, 3072):
            raise AssertionError(f"skin_Sodium state{state}: expected 3072x3072, got {skin.size}")
        for row in range(3):
            for col in range(3):
                rows.append((f"skin_Sodium_state{state}.png", row, col, audit_pair(reference, skin, row, col)))

    print("file                         cell  silhouette  canopy/teal  changed     texture-luma")
    print("                             (r,c)       diff          diff          all px       armour")
    for name, row, col, metrics in rows:
        print(f"{name:30} {row},{col}   {metrics['silhouette']:8.3f}%   {metrics['canopy']:8.3f}%   {metrics['changed']:8.3f}%   {metrics['texture']:8.3f}")

    for name, row, col, metrics in rows:
        assert metrics["silhouette"] < 1.5, (name, row, col, "silhouette", metrics)
        assert metrics["canopy"] < 6.0, (name, row, col, "canopy", metrics)
        assert metrics["changed"] > 12.0, (name, row, col, "changed", metrics)
        assert metrics["texture"] > 6.0, (name, row, col, "texture", metrics)

    assert_flat_background_and_no_pink()
    print("\nPASS: all 27 cells satisfy the Sodium repaint thresholds.")


if __name__ == "__main__":
    main()
