"""Audit Bone sheets against the approved 3x3 references."""

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)
CELL = 512
OUT_CELL = 1024


def cell(image, row, col, size):
    return image.crop((col * size, row * size, (col + 1) * size, (row + 1) * size))


def mask(image):
    return [pixel != GREEN for pixel in image.getdata()]


def canopy_region(image):
    """Find the cyan/blue canopy pixels used as an invariant audit region."""
    result = []
    for r, g, b in image.getdata():
        result.append(b > r * 1.15 and b > g * 1.02 and b > 70 and g > 35)
    return result


def percent(value, total):
    return 100.0 * value / total if total else 0.0


def main():
    failures = []
    print("file             cell       silhouette diff   canopy/blue diff   changed pixels")
    for state in range(3):
        reference = Image.open(ROOT / f"skinref_state{state}.png").convert("RGB")
        output = Image.open(ROOT / f"skin_Bone_state{state}.png").convert("RGB")
        if reference.size != (1536, 1536):
            raise AssertionError(f"reference state{state}: expected 1536x1536, got {reference.size}")
        if output.size != (3072, 3072):
            raise AssertionError(f"output state{state}: expected 3072x3072, got {output.size}")

        for row in range(3):
            for col in range(3):
                ref_cell = cell(reference, row, col, CELL)
                out_cell = cell(output, row, col, OUT_CELL)
                ref_up = ref_cell.resize((OUT_CELL, OUT_CELL), Image.Resampling.NEAREST)
                ref_mask = mask(ref_up)
                out_mask = mask(out_cell)
                ref_blue = canopy_region(ref_up)
                ref_pixels = list(ref_up.getdata())
                out_pixels = list(out_cell.getdata())
                silhouette_diff = sum(a != b for a, b in zip(ref_mask, out_mask))
                blue_diff = sum(
                    ref_blue[index] and ref_pixels[index] != out_pixels[index]
                    for index in range(OUT_CELL * OUT_CELL)
                )
                changed = sum(a != b for a, b in zip(ref_pixels, out_pixels))
                silhouette_pct = percent(silhouette_diff, sum(ref_mask))
                blue_pct = percent(blue_diff, sum(ref_blue))
                changed_pct = percent(changed, OUT_CELL * OUT_CELL)
                label = f"state{state}        ({row},{col})"
                print(f"{label:<20} {silhouette_pct:8.3f}%          {blue_pct:8.3f}%          {changed_pct:8.3f}%")
                if silhouette_pct >= 1.5:
                    failures.append(f"state{state} ({row},{col}) silhouette {silhouette_pct:.3f}%")
                if blue_pct >= 6.0:
                    failures.append(f"state{state} ({row},{col}) canopy {blue_pct:.3f}%")
                if changed_pct <= 12.0:
                    failures.append(f"state{state} ({row},{col}) changed {changed_pct:.3f}%")

    if failures:
        raise SystemExit("FAIL: " + "; ".join(failures))
    print("\nPASS: all 27 cells meet the requested thresholds")


if __name__ == "__main__":
    main()
