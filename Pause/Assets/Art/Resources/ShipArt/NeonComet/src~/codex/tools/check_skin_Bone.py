#!/usr/bin/env python3
"""Validate Bone sheets against the approved 3x3 references."""

from pathlib import Path
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)


def upscaled_cell(im, row, col):
    cell = im.crop((col * 512, row * 512, (col + 1) * 512, (row + 1) * 512))
    return cell.resize((1024, 1024), Image.Resampling.NEAREST)


def metrics(ref, out):
    rp = ref.load()
    op = out.load()
    silhouette_diff = 0
    changed = 0
    blue_count = 0
    blue_error = 0
    total = 1024 * 1024
    for y in range(1024):
        for x in range(1024):
            a = rp[x, y]
            b = op[x, y]
            a_solid = a != GREEN
            b_solid = b != GREEN
            silhouette_diff += a_solid != b_solid
            changed += a != b

            # Canopy/blue-region audit: measure normalized RGB error only over
            # reference blue/cyan pixels, which must remain unchanged.
            ar, ag, ab = a
            if ab > ar * 1.15 and ab > ag * 1.02 and ab > 45:
                blue_count += 1
                blue_error += sum(abs(a[i] - b[i]) for i in range(3)) / (3 * 255)

    silhouette_pct = 100.0 * silhouette_diff / total
    changed_pct = 100.0 * changed / total
    blue_pct = 100.0 * blue_error / blue_count if blue_count else 0.0
    return silhouette_pct, blue_pct, changed_pct


def main():
    all_rows = []
    for state in range(3):
        ref_sheet = Image.open(ROOT / f"skinref_state{state}.png").convert("RGB")
        out_sheet = Image.open(ROOT / f"skin_Bone_state{state}.png").convert("RGB")
        if out_sheet.size != (3072, 3072):
            raise SystemExit(f"state{state}: expected 3072x3072, got {out_sheet.size}")
        for row in range(3):
            for col in range(3):
                result = metrics(upscaled_cell(ref_sheet, row, col), out_sheet.crop((col * 1024, row * 1024, (col + 1) * 1024, (row + 1) * 1024)))
                all_rows.append((state, row, col, *result))
                silhouette, blue, changed = result
                print(f"state{state} cell({row},{col}): silhouette={silhouette:.3f}% canopy_blue={blue:.3f}% changed={changed:.3f}%")

    max_silhouette = max(row[3] for row in all_rows)
    max_blue = max(row[4] for row in all_rows)
    min_changed = min(row[5] for row in all_rows)
    passed = max_silhouette < 1.5 and max_blue < 6.0 and min_changed > 12.0
    print(f"SUMMARY max_silhouette={max_silhouette:.3f}% max_canopy_blue={max_blue:.3f}% min_changed={min_changed:.3f}% PASS={passed}")
    if not passed:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
