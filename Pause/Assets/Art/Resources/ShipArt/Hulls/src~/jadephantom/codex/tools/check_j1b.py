"""Validate and print the J1b pose-sheet thresholds."""

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)
SIZE = 1024
SHEET_SIZE = (3072, 2048)


def load(path):
    image = Image.open(path).convert("RGB")
    if image.size != SHEET_SIZE:
        raise ValueError(f"{path.name}: expected 3072x2048, got {image.size}")
    return image


def key(path):
    image = Image.open(path).convert("RGB")
    if image.size != (SIZE, SIZE):
        raise ValueError(f"{path.name}: expected 1024x1024")
    return image


def cell(sheet, index):
    return sheet.crop(((index % 3) * SIZE, (index // 3) * SIZE, (index % 3 + 1) * SIZE, (index // 3 + 1) * SIZE))


def changed(a, b, threshold=60):
    aa = np.asarray(a, dtype=np.int16)
    bb = np.asarray(b, dtype=np.int16)
    return int(np.count_nonzero(np.abs(aa - bb).sum(axis=2) > threshold))


def diff_pct(a, b, threshold=60):
    return 100.0 * changed(a, b, threshold) / (SIZE * SIZE)


def non_green(image):
    return np.any(np.asarray(image) != np.asarray(GREEN), axis=2)


def silhouette_difference(a, b):
    am, bm = non_green(a), non_green(b)
    union = np.count_nonzero(np.logical_or(am, bm))
    return 100.0 * np.count_nonzero(am != bm) / union if union else 0.0


def shifted(image, dx, dy):
    result = Image.new("RGB", (SIZE, SIZE), GREEN)
    left = max(0, dx)
    top = max(0, dy)
    src_left = max(0, -dx)
    src_top = max(0, -dy)
    width = SIZE - abs(dx)
    height = SIZE - abs(dy)
    if width > 0 and height > 0:
        result.paste(image.crop((src_left, src_top, src_left + width, src_top + height)), (left, top))
    return result


def best_translation(key_image, bank_image):
    reference = np.asarray(key_image, dtype=np.uint8)
    bank = np.asarray(bank_image, dtype=np.uint8)
    best = None
    for dy in range(-12, 13):
        for dx in range(-12, 13):
            candidate = np.empty_like(reference)
            candidate[:] = GREEN
            left = max(0, dx)
            top = max(0, dy)
            src_left = max(0, -dx)
            src_top = max(0, -dy)
            width = SIZE - abs(dx)
            height = SIZE - abs(dy)
            if width > 0 and height > 0:
                candidate[top:top + height, left:left + width] = reference[src_top:src_top + height, src_left:src_left + width]
            score = int(np.count_nonzero(np.abs(candidate.astype(np.int16) - bank.astype(np.int16)).sum(axis=2) > 60))
            if best is None or score < best[0]:
                best = (score, dx, dy, candidate)
    return best


def canopy_mask(image):
    array = np.asarray(image)
    r, g, b = array[:, :, 0], array[:, :, 1], array[:, :, 2]
    mask = (b > 100) & (g > 70) & (b > r * 1.2)
    region = np.zeros((SIZE, SIZE), dtype=bool)
    region[145:335, 445:575] = True
    return mask & region


def canopy_diff_pct(a, b, reference):
    mask = canopy_mask(reference)
    total = int(np.count_nonzero(mask))
    aa = np.asarray(a, dtype=np.int16)
    bb = np.asarray(b, dtype=np.int16)
    differences = int(np.count_nonzero(mask & (np.abs(aa - bb).sum(axis=2) > 60)))
    return 100.0 * differences / total if total else 0.0


def main():
    sheets = [load(ROOT / f"hull_redo_state{state}.png") for state in range(3)]
    keys = [key(ROOT / f"key_state{state}.png") for state in range(3)]
    print("file                 c0%     c1%     c2%     c3%     c4%   canopy1% canopy2%  bank-residual(L/R)%  silhouette(L/R)%")
    all_ok = True
    for state, (sheet, reference) in enumerate(zip(sheets, keys)):
        cells = [cell(sheet, i) for i in range(6)]
        values = [diff_pct(cells[i], reference) for i in range(5)]
        canopy1 = canopy_diff_pct(cells[1], reference, reference)
        canopy2 = canopy_diff_pct(cells[2], reference, reference)
        residuals, silhouettes = [], []
        for bank in (cells[3], cells[4]):
            best = best_translation(reference, bank)
            residuals.append(100.0 * best[0] / (SIZE * SIZE))
            silhouettes.append(silhouette_difference(best[3], bank))
        print(f"hull_redo_state{state}.png {values[0]:7.3f} {values[1]:7.3f} {values[2]:7.3f} {values[3]:7.3f} {values[4]:7.3f} {canopy1:9.3f} {canopy2:9.3f}   {residuals[0]:7.3f}/{residuals[1]:7.3f}       {silhouettes[0]:7.3f}/{silhouettes[1]:7.3f}")
        if np.any(np.asarray(cells[5]) != np.asarray(GREEN)):
            print(f"FAIL state{state}: cell 5 is not empty")
            all_ok = False
        if values[0] < 0.4 or values[1] < 0.8 or values[2] <= 0:
            print(f"FAIL state{state}: light/glint threshold")
            all_ok = False
        if diff_pct(cells[1], cells[2]) <= 0:
            print(f"FAIL state{state}: cell 2 does not differ from cell 1")
            all_ok = False
        if min(residuals) < 5.0 or min(silhouettes) < 3.0:
            print(f"FAIL state{state}: bank thresholds")
            all_ok = False
    if not all_ok:
        raise SystemExit(1)
    print("PASS: all J1b dimensions, empty cells, animation, translation-residual, and silhouette thresholds")


if __name__ == "__main__":
    main()
