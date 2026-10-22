from pathlib import Path
import warnings

import numpy as np
from PIL import Image


warnings.filterwarnings("ignore", category=DeprecationWarning)


ROOT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
SIZE = 1024
SHEET_SIZE = (3072, 2048)


def load(path):
    image = Image.open(path).convert("RGB")
    if image.size != SHEET_SIZE:
        raise AssertionError(f"{path.name}: expected 3072x2048, got {image.size}")
    return image


def key_load(index):
    image = Image.open(ROOT / f"key_state{index}.png").convert("RGB")
    if image.size != (SIZE, SIZE):
        raise AssertionError(f"key_state{index}.png: expected 1024x1024")
    return image


def cell(sheet, index):
    left = (index % 3) * SIZE
    top = (index // 3) * SIZE
    return sheet.crop((left, top, left + SIZE, top + SIZE))


def diff_mask(a, b, threshold=60):
    return [sum(abs(x - y) for x, y in zip(pa, pb)) > threshold
            for pa, pb in zip(a.getdata(), b.getdata())]


def non_green(image):
    return [pixel != GREEN for pixel in image.getdata()]


def percent(bits, denominator=None):
    denominator = denominator or len(bits)
    return 100.0 * sum(bits) / denominator if denominator else 0.0


def canopy_mask(key):
    bits = []
    for y in range(SIZE):
        for x in range(SIZE):
            inside = ((x - 520) / 74) ** 2 + ((y - 345) / 130) ** 2 < 1.25
            r, g, b = key.getpixel((x, y))
            bits.append(inside and b > r * 1.05 and b > 45 and g > 35)
    return bits


def translated_diff(key_array, other_array, other_is_non_green, dx, dy):
    source_x0 = max(0, -dx)
    source_x1 = min(SIZE, SIZE - dx)
    source_y0 = max(0, -dy)
    source_y1 = min(SIZE, SIZE - dy)
    target_x0 = source_x0 + dx
    target_x1 = source_x1 + dx
    target_y0 = source_y0 + dy
    target_y1 = source_y1 + dy
    inside = np.abs(
        key_array[source_y0:source_y1, source_x0:source_x1]
        - other_array[target_y0:target_y1, target_x0:target_x1]
    ).sum(axis=2) > 60
    outside = other_is_non_green.copy()
    outside[target_y0:target_y1, target_x0:target_x1] = False
    differences = int(inside.sum()) + int(outside.sum())
    return 100.0 * differences / (SIZE * SIZE)


def best_translation_residual(key, other):
    key_array = np.asarray(key, dtype=np.int16)
    other_array = np.asarray(other, dtype=np.int16)
    other_is_non_green = np.abs(other_array - np.asarray(GREEN, dtype=np.int16)).sum(axis=2) > 60
    best = None
    best_shift = None
    coarse = [(dx, dy) for dy in range(-24, 25, 4) for dx in range(-24, 25, 4)]
    for dx, dy in coarse:
        value = translated_diff(key_array, other_array, other_is_non_green, dx, dy)
        if best is None or value < best:
            best = value
            best_shift = (dx, dy)
    center_x, center_y = best_shift
    for dy in range(center_y - 4, center_y + 5):
        for dx in range(center_x - 4, center_x + 5):
            value = translated_diff(key_array, other_array, other_is_non_green, dx, dy)
            if best is None or value < best:
                best = value
                best_shift = (dx, dy)
    return best, best_shift


def translated_mask(key_mask, dx, dy):
    result = [False] * (SIZE * SIZE)
    for y in range(SIZE):
        source_y = y - dy
        for x in range(SIZE):
            source_x = x - dx
            if 0 <= source_x < SIZE and 0 <= source_y < SIZE:
                result[y * SIZE + x] = key_mask[source_y * SIZE + source_x]
    return result


def silhouette_difference(a, b):
    union = sum(x or y for x, y in zip(a, b))
    return 100.0 * sum(x != y for x, y in zip(a, b)) / union if union else 0.0


def main():
    sheets = [load(ROOT / f"hull_redo_state{index}.png") for index in range(3)]
    keys = [key_load(index) for index in range(3)]
    failures = []
    print("file                 cell0   cell1   cell2   cell3   cell4   canopy1 canopy2")
    for index, (sheet, key) in enumerate(zip(sheets, keys)):
        key_canopy = canopy_mask(key)
        values = []
        for cell_index in range(5):
            values.append(percent(diff_mask(key, cell(sheet, cell_index))))
        canopy_values = []
        for cell_index in (1, 2):
            bits = diff_mask(key, cell(sheet, cell_index))
            canopy_values.append(percent([a and b for a, b in zip(bits, key_canopy)], sum(key_canopy)))
        print(f"state{index}               " + " ".join(f"{v:7.3f}%" for v in values) +
              "  " + " ".join(f"{v:7.3f}%" for v in canopy_values))
        if values[0] < 0.4:
            failures.append(f"state{index} cell0 diff {values[0]:.3f}% < 0.4%")
        if values[1] < 0.8:
            failures.append(f"state{index} cell1 diff {values[1]:.3f}% < 0.8%")
        cell2_vs_cell1 = percent(diff_mask(cell(sheet, 1), cell(sheet, 2)))
        if cell2_vs_cell1 <= 0.0:
            failures.append(f"state{index} cell2 does not differ from cell1")
        bank_cells = [cell(sheet, 3), cell(sheet, 4)]
        for bank_index, bank in zip((3, 4), bank_cells):
            residual, shift = best_translation_residual(key, bank)
            silhouette = silhouette_difference(non_green(key), non_green(bank))
            print(f"  cell{bank_index} bank residual {residual:7.3f}% after {shift!s:<10} silhouette {silhouette:7.3f}%")
            if residual < 5.0:
                failures.append(f"state{index} cell{bank_index} residual {residual:.3f}% < 5%")
            if silhouette < 3.0:
                failures.append(f"state{index} cell{bank_index} silhouette {silhouette:.3f}% < 3%")
        print(f"  cell2 vs cell1 {cell2_vs_cell1:7.3f}%")
    if failures:
        raise SystemExit("\nFAIL\n" + "\n".join(failures))
    print("\nPASS")


if __name__ == "__main__":
    main()
