from __future__ import annotations

import colorsys
from pathlib import Path

from PIL import Image, ImageFilter


ROOT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
REF_SIZE = 1536
CELL = 512
OUT_CELL = 1024
OUT_SIZE = 3072

CASES = (
    ("skin_Night_state0.png", "skinref_state0.png", tuple(range(9))),
    ("skin_Night_state1.png", "skinref_state1.png", (0,)),
    ("skin_Night_state2.png", "skinref_state2.png", (0,)),
)


def hsv(pixel: tuple[int, int, int]) -> tuple[float, float, float]:
    r, g, b = pixel
    h, s, v = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
    return h * 360.0, s, v


def is_warm(pixel: tuple[int, int, int]) -> bool:
    r, g, b = pixel
    return r > g > b and r - b > 40


def is_cyan(pixel: tuple[int, int, int]) -> bool:
    r, g, b = pixel
    return g >= 95 and b >= 95 and b >= r + 28 and g >= r + 18


def crop(image: Image.Image, index: int, size: int) -> Image.Image:
    x = (index % 3) * size
    y = (index // 3) * size
    return image.crop((x, y, x + size, y + size)).convert("RGB")


def changed_mask(reference: Image.Image, intact: Image.Image) -> Image.Image:
    mask = Image.new("1", (CELL, CELL))
    ref_pixels = reference.load()
    intact_pixels = intact.load()
    mask_pixels = mask.load()
    for y in range(CELL):
        for x in range(CELL):
            mask_pixels[x, y] = ref_pixels[x, y] != intact_pixels[x, y]
    return mask.filter(ImageFilter.MaxFilter(7))


def warm_outside_allowed(
    output: Image.Image,
    reference: Image.Image,
    damage: Image.Image,
) -> tuple[float, int, int]:
    """Return warm pixels outside lamp/lights/nozzle/damage as % of hull."""
    output_pixels = output.load()
    reference_pixels = reference.load()
    damage_pixels = damage.load()
    hull = 0
    warm_outside = 0

    for y in range(CELL):
        for x in range(CELL):
            reference_pixel = reference_pixels[x, y]
            if reference_pixel == GREEN:
                continue
            hull += 1
            output_pixel = output_pixels[x, y]
            if not is_warm(output_pixel):
                continue

            hue, saturation, _ = hsv(reference_pixel)
            lamp = (x - 256) ** 2 + (y - 292) ** 2 <= 13 ** 2
            red_light_or_damage = hue < 18.0 and saturation > 0.55
            nozzle = y > 450 and hue < 55.0 and saturation > 0.25
            reference_damage = damage_pixels[x, y]
            if not (lamp or red_light_or_damage or nozzle or reference_damage):
                warm_outside += 1

    return warm_outside / max(hull, 1) * 100.0, warm_outside, hull


def report(
    output: Image.Image,
    reference: Image.Image,
    intact: Image.Image,
    name: str,
    index: int,
) -> bool:
    reference_scaled = reference.resize((OUT_CELL, OUT_CELL), Image.Resampling.NEAREST)
    output_pixels = list(output.getdata())
    reference_pixels = list(reference_scaled.getdata())
    reference_mask = [pixel != GREEN for pixel in reference_pixels]
    output_mask = [pixel != GREEN for pixel in output_pixels]
    hull_pixels = max(1, sum(reference_mask))

    silhouette = sum(a != b for a, b in zip(reference_mask, output_mask)) / hull_pixels * 100.0
    cyan_mask = [is_cyan(pixel) for pixel in reference_pixels]
    cyan_pixels = max(1, sum(cyan_mask))
    canopy_difference = (
        sum(reference_pixels[i] != output_pixels[i] for i, selected in enumerate(cyan_mask) if selected)
        / cyan_pixels
        * 100.0
    )
    changed = sum(a != b for a, b in zip(reference_pixels, output_pixels)) / len(reference_pixels) * 100.0
    warm_percent, warm_count, hull_count = warm_outside_allowed(
        output.resize((CELL, CELL), Image.Resampling.NEAREST),
        reference,
        changed_mask(reference, intact),
    )

    passed = (
        silhouette < 1.5
        and canopy_difference < 6.0
        and changed > 12.0
        and warm_percent < 0.3
    )
    status = "PASS" if passed else "FAIL"
    print(
        f"{status} {name} cell {index}: "
        f"silhouette {silhouette:.4f}% | "
        f"canopy/cyan {canopy_difference:.4f}% | "
        f"changed {changed:.4f}% | "
        f"warm outside allowed {warm_percent:.4f}% "
        f"({warm_count}/{hull_count} hull pixels)"
    )
    return passed


def main() -> None:
    all_pass = True
    for output_name, reference_name, indices in CASES:
        output_sheet = Image.open(ROOT / output_name).convert("RGB")
        reference_sheet = Image.open(ROOT / reference_name).convert("RGB")
        intact_sheet = Image.open(ROOT / "skinref_state0.png").convert("RGB")
        if output_sheet.size != (OUT_SIZE, OUT_SIZE):
            raise SystemExit(f"FAIL: {output_name} is {output_sheet.size}, expected 3072x3072")
        if reference_sheet.size != (REF_SIZE, REF_SIZE):
            raise SystemExit(f"FAIL: {reference_name} is {reference_sheet.size}, expected 1536x1536")

        for index in indices:
            output = crop(output_sheet, index, OUT_CELL)
            reference = crop(reference_sheet, index, CELL)
            intact = crop(intact_sheet, index, CELL)
            all_pass &= report(output, reference, intact, output_name, index)

        if indices == (0,):
            for index in range(1, 9):
                empty = crop(output_sheet, index, OUT_CELL)
                if any(pixel != GREEN for pixel in empty.getdata()):
                    print(f"FAIL {output_name} cell {index}: required-empty cell is not pure green")
                    all_pass = False

    if not all_pass:
        raise SystemExit("FAIL: one or more Night constraints did not pass")

    print("PASS: dimensions, empty-cell contract, silhouette, cyan canopy, colour change, and warm-pixel thresholds")
    print("Caveats: warm-pixel exclusions are derived from reference emitter/damage masks and a fixed lamp center; hull percentage uses all non-green reference pixels as the denominator.")


if __name__ == "__main__":
    main()
