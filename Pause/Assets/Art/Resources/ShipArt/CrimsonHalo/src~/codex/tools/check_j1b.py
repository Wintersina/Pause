from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = np.array([0, 255, 0], dtype=np.int16)
THRESHOLD = 60
CELL = 1024


def load(path: Path) -> np.ndarray:
    image = Image.open(path).convert("RGB")
    if image.size != (3072, 2048):
        raise ValueError(f"{path.name}: expected 3072x2048, got {image.size}")
    return np.asarray(image, dtype=np.int16)


def cells(sheet: np.ndarray) -> list[np.ndarray]:
    return [sheet[y * CELL:(y + 1) * CELL, x * CELL:(x + 1) * CELL]
            for y in range(2) for x in range(3)]


def green_mask(image: np.ndarray) -> np.ndarray:
    return np.all(image == GREEN, axis=2)


def non_green(image: np.ndarray) -> np.ndarray:
    return ~green_mask(image)


def diff_mask(left: np.ndarray, right: np.ndarray) -> np.ndarray:
    return np.sum(np.abs(left - right), axis=2) > THRESHOLD


def percent(mask: np.ndarray) -> float:
    return float(mask.mean() * 100.0)


def translated(reference: np.ndarray, dx: int, dy: int) -> np.ndarray:
    shifted = np.empty_like(reference)
    shifted[...] = GREEN
    height, width = reference.shape[:2]
    src_x0, src_x1 = max(0, -dx), min(width, width - dx)
    src_y0, src_y1 = max(0, -dy), min(height, height - dy)
    dst_x0, dst_x1 = src_x0 + dx, src_x1 + dx
    dst_y0, dst_y1 = src_y0 + dy, src_y1 + dy
    shifted[dst_y0:dst_y1, dst_x0:dst_x1] = reference[src_y0:src_y1, src_x0:src_x1]
    return shifted


def best_translation(reference: np.ndarray, target: np.ndarray, limit: int = 80) -> tuple[int, int, np.ndarray]:
    """Find the best integer translation, using a coarse pass then a local full-res pass."""
    stride = 4
    small_reference = reference[::stride, ::stride]
    small_target = target[::stride, ::stride]
    coarse_limit = limit // stride
    best = None
    for dy in range(-coarse_limit, coarse_limit + 1):
        for dx in range(-coarse_limit, coarse_limit + 1):
            shifted = translated(small_reference, dx, dy)
            score = int(np.sum(diff_mask(shifted, small_target)))
            if best is None or score < best[0]:
                best = (score, dx, dy)
    assert best is not None
    coarse_dx, coarse_dy = best[1] * stride, best[2] * stride

    best_full = None
    for dy in range(coarse_dy - 4, coarse_dy + 5):
        for dx in range(coarse_dx - 4, coarse_dx + 5):
            shifted = translated(reference, dx, dy)
            score = int(np.sum(diff_mask(shifted, target)))
            if best_full is None or score < best_full[0]:
                best_full = (score, dx, dy, shifted)
    assert best_full is not None
    return best_full[1], best_full[2], best_full[3]


def canopy_mask(key: np.ndarray) -> np.ndarray:
    red, green, blue = [key[..., i] for i in range(3)]
    yy, xx = np.indices(key.shape[:2])
    region = (xx >= 445) & (xx < 580) & (yy >= 205) & (yy < 475)
    teal = (blue > 90) & (green > 70) & (blue >= red + 35) & (green >= red + 25)
    return region & teal & non_green(key)


def silhouette_difference(aligned_key: np.ndarray, bank: np.ndarray) -> float:
    left = non_green(aligned_key)
    right = non_green(bank)
    union = left | right
    return float(np.count_nonzero(left ^ right) / max(np.count_nonzero(union), 1) * 100.0)


def check_state(state: int) -> tuple[list[float], float, float, float, float, float, bool]:
    key = np.asarray(Image.open(ROOT / f"key_state{state}.png").convert("RGB"), dtype=np.int16)
    sheet = load(ROOT / f"hull_redo_state{state}.png")
    frame = cells(sheet)
    if np.any(non_green(frame[5])):
        raise ValueError(f"state{state}: cell 5 is not empty")

    all_diffs = [percent(diff_mask(frame[i], key)) for i in range(5)]
    canopy = canopy_mask(key)
    canopy_diffs = [percent(diff_mask(frame[i], key)[canopy]) for i in (1, 2)]
    cell2_vs_cell1 = percent(diff_mask(frame[1], frame[2]))
    bank_metrics = []
    for i in (3, 4):
        _, _, aligned = best_translation(key, frame[i])
        bank_metrics.extend([
            percent(diff_mask(aligned, frame[i])),
            silhouette_difference(aligned, frame[i]),
        ])

    passed = (
        all_diffs[0] >= 0.4
        and all_diffs[1] >= 0.8
        and cell2_vs_cell1 > 0.0
        and all(value >= 5.0 for value in (bank_metrics[0], bank_metrics[2]))
        and all(value >= 3.0 for value in (bank_metrics[1], bank_metrics[3]))
    )
    return all_diffs, canopy_diffs[0], canopy_diffs[1], cell2_vs_cell1, bank_metrics[0], bank_metrics[1], passed


def main() -> None:
    print("state | cell0  cell1  cell2  cell3  cell4 | canopy1 canopy2 | cell2-v-cell1 | bankL residual/silhouette | bankR residual/silhouette | result")
    print("------|-------------------------------------|-----------------|---------------|--------------------------|--------------------------|-------")
    overall = True
    for state in range(3):
        diffs, c1, c2, cell2_vs_cell1, bank_left_residual, bank_left_silhouette, passed = check_state(state)
        # Re-run the right-bank metrics for the printed table without changing the checker logic.
        key = np.asarray(Image.open(ROOT / f"key_state{state}.png").convert("RGB"), dtype=np.int16)
        frame = cells(load(ROOT / f"hull_redo_state{state}.png"))
        _, _, aligned_right = best_translation(key, frame[4])
        bank_right_residual = percent(diff_mask(aligned_right, frame[4]))
        bank_right_silhouette = silhouette_difference(aligned_right, frame[4])
        overall &= passed
        print(
            f"  {state}   | "
            f"{diffs[0]:6.2f} {diffs[1]:6.2f} {diffs[2]:6.2f} {diffs[3]:6.2f} {diffs[4]:6.2f} | "
            f"{c1:7.2f} {c2:7.2f} | "
            f"{cell2_vs_cell1:13.2f} | "
            f"{bank_left_residual:7.2f}% / {bank_left_silhouette:7.2f}% | "
            f"{bank_right_residual:7.2f}% / {bank_right_silhouette:7.2f}% | "
            f"{'PASS' if passed else 'FAIL'}"
        )
    print(f"overall: {'PASS' if overall else 'FAIL'}")
    raise SystemExit(0 if overall else 1)


if __name__ == "__main__":
    main()
