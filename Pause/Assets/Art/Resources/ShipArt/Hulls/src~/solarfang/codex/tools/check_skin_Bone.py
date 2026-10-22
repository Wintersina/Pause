from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
REF_SIZE = 512
OUT_SIZE = 1024
FILES = [
    (ROOT / "skinref_state0.png", ROOT / "skin_Bone_state0.png"),
    (ROOT / "skinref_state1.png", ROOT / "skin_Bone_state1.png"),
    (ROOT / "skinref_state2.png", ROOT / "skin_Bone_state2.png"),
]


def is_green(pixel):
    r, g, b = pixel
    return pixel == GREEN or (g > r * 1.05 and g > b * 1.05)


def is_blue_region(pixel):
    r, g, b = pixel
    return not is_green(pixel) and b >= r * 1.08 and b >= g * 0.92 and b > 42


def cell(image, row, col, size):
    return image.crop((col * size, row * size, (col + 1) * size, (row + 1) * size))


def pixels(image):
    get_flattened_data = getattr(image, "get_flattened_data", None)
    return list(get_flattened_data()) if get_flattened_data else list(image.getdata())


def load_pair(reference_path, output_path):
    reference = Image.open(reference_path).convert("RGB")
    output = Image.open(output_path).convert("RGB")
    if reference.size != (1536, 1536):
        raise AssertionError(f"{reference_path.name}: expected 1536x1536 reference")
    if output.size != (3072, 3072):
        raise AssertionError(f"{output_path.name}: expected 3072x3072 output")
    return reference, output


def compare(reference, output):
    ref_pixels = pixels(reference)
    out_pixels = pixels(output)
    ref_mask = [not is_green(pixel) for pixel in ref_pixels]
    out_mask = [not is_green(pixel) for pixel in out_pixels]
    union = sum(a or b for a, b in zip(ref_mask, out_mask))
    silhouette = 100.0 * sum(a != b for a, b in zip(ref_mask, out_mask)) / union if union else 0.0

    blue = [is_blue_region(pixel) for pixel in ref_pixels]
    blue_count = sum(blue)
    blue_diff = (
        100.0 * sum(a != b for a, b, is_blue in zip(ref_pixels, out_pixels, blue) if is_blue) / blue_count
        if blue_count
        else 0.0
    )
    changed = 100.0 * sum(a != b for a, b in zip(ref_pixels, out_pixels)) / len(ref_pixels)
    return silhouette, blue_diff, changed


def main():
    failures = []
    rows = []
    print("file                 cell  silhouette_diff  canopy_blue_diff  changed_fraction")
    print("-------------------  ----  ----------------  -----------------  ----------------")
    for reference_path, output_path in FILES:
        reference, output = load_pair(reference_path, output_path)
        for row in range(3):
            for col in range(3):
                ref = cell(reference, row, col, REF_SIZE).resize((OUT_SIZE, OUT_SIZE), Image.Resampling.NEAREST)
                out = cell(output, row, col, OUT_SIZE)
                silhouette, blue_diff, changed = compare(ref, out)
                cell_number = row * 3 + col + 1
                label = f"{output_path.name:<20} {cell_number:>2}"
                print(f"{label}  {silhouette:>16.4f}%  {blue_diff:>16.4f}%  {changed:>15.4f}%")
                rows.append((output_path.name, cell_number, silhouette, blue_diff, changed))
                if silhouette >= 1.5:
                    failures.append(f"{output_path.name} cell {cell_number}: silhouette diff >= 1.5%")
                if blue_diff >= 6.0:
                    failures.append(f"{output_path.name} cell {cell_number}: canopy/blue diff >= 6%")
                if changed <= 12.0:
                    failures.append(f"{output_path.name} cell {cell_number}: changed fraction <= 12%")

    if failures:
        raise SystemExit("\nFAIL\n" + "\n".join(failures))

    silhouettes = [row[2] for row in rows]
    blue_diffs = [row[3] for row in rows]
    changed = [row[4] for row in rows]
    print(
        "\nPASS: all 27 cells meet thresholds; "
        f"max silhouette={max(silhouettes):.4f}%, "
        f"max canopy/blue={max(blue_diffs):.4f}%, "
        f"min changed={min(changed):.4f}%"
    )


if __name__ == "__main__":
    main()
