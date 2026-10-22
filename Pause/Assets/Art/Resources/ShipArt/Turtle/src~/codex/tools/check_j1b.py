from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
GREEN = np.array([0, 255, 0], dtype=np.int16)
SIZE = 1024


def load_sheet():
    image = Image.open(ROOT / "hull_redo_state0.png").convert("RGB")
    if image.size != (3072, 2048):
        raise SystemExit(f"hull_redo_state0.png: expected 3072x2048, got {image.size}")
    array = np.asarray(image, dtype=np.int16)
    return [array[y:y + SIZE, x:x + SIZE] for y in (0, SIZE) for x in (0, SIZE, SIZE * 2)]


def changed(a, b):
    return (np.abs(a - b).sum(axis=-1) > 60)


def silhouette(image):
    return np.any(image != GREEN, axis=2)


def bounds(mask):
    ys, xs = np.where(mask)
    return int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())


def percent(value, total):
    return value * 100.0 / total


def canopy_mask(key):
    mask = Image.new("1", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).polygon(
        [(512, 184), (560, 208), (560, 292), (512, 320), (464, 292), (464, 208)],
        fill=1,
    )
    mask = np.asarray(mask, dtype=bool)
    cyan = (key[:, :, 2] > 70) & (key[:, :, 1] > 70) & (key[:, :, 2] > key[:, :, 0] * 1.12)
    return mask & cyan


def shifted_key(key, dx, dy):
    height, width = key.shape[:2]
    shifted = np.empty_like(key)
    shifted[:] = GREEN
    x0 = max(0, dx)
    x1 = min(width, width + dx)
    y0 = max(0, dy)
    y1 = min(height, height + dy)
    shifted[y0:y1, x0:x1] = key[y0 - dy:y1 - dy, x0 - dx:x1 - dx]
    return shifted


def best_translation_residual(key, bank):
    # Coarse search over the full requested window, then exact refinement.
    # This keeps the checker quick while still evaluating integer-pixel shifts.
    small_key = key[::4, ::4]
    small_bank = bank[::4, ::4]
    coarse_best = 100.0
    coarse_offset = (0, 0)
    for dy in range(-8, 9):
        for dx in range(-8, 9):
            residual = percent(
                np.count_nonzero(changed(shifted_key(small_key, dx, dy), small_bank)),
                small_key.shape[0] * small_key.shape[1],
            )
            if residual < coarse_best:
                coarse_best = residual
                coarse_offset = (dx * 4, dy * 4)
    best = 100.0
    best_offset = coarse_offset
    for dy in range(coarse_offset[1] - 4, coarse_offset[1] + 5):
        for dx in range(coarse_offset[0] - 4, coarse_offset[0] + 5):
            residual = percent(np.count_nonzero(changed(shifted_key(key, dx, dy), bank)), SIZE * SIZE)
            if residual < best:
                best = residual
                best_offset = (dx, dy)
    return best, best_offset


def main():
    cells = load_sheet()
    key_image = Image.open(ROOT / "key_state0.png").convert("RGB")
    if key_image.size != (SIZE, SIZE):
        raise SystemExit(f"key_state0.png: expected 1024x1024, got {key_image.size}")
    key = np.asarray(key_image, dtype=np.int16)
    key_mask = silhouette(key)
    key_box = bounds(key_mask)
    key_width = key_box[2] - key_box[0] + 1
    key_height = key_box[3] - key_box[1] + 1
    canopy = canopy_mask(key)

    print("cell | diff from key (>60 summed RGB) | canopy diff (>60) | silhouette bbox")
    print("-----+-------------------------------+--------------------+----------------")
    for index, cell in enumerate(cells[:5]):
        diff = percent(np.count_nonzero(changed(key, cell)), SIZE * SIZE)
        canopy_diff = percent(np.count_nonzero(changed(key[canopy], cell[canopy])), np.count_nonzero(canopy)) if index in (1, 2) else None
        box = bounds(silhouette(cell))
        canopy_text = f"{canopy_diff:8.3f}%" if canopy_diff is not None else "       --"
        print(f" {index}   | {diff:10.3f}%                  | {canopy_text}       | {box}")

    print("\nbank metrics")
    print("cell | width/key | height/key | residual after best integer translation | mask diff/key")
    print("-----+-----------+------------+----------------------------------------+--------------")
    bank_metrics = []
    for index in (3, 4):
        mask = silhouette(cells[index])
        box = bounds(mask)
        width_ratio = (box[2] - box[0] + 1) / key_width * 100.0
        height_ratio = (box[3] - box[1] + 1) / key_height * 100.0
        residual, offset = best_translation_residual(key, cells[index])
        mask_diff = percent(np.count_nonzero(key_mask != mask), np.count_nonzero(key_mask))
        bank_metrics.append((width_ratio, height_ratio, residual, mask_diff))
        print(f" {index}   | {width_ratio:8.3f}% | {height_ratio:9.3f}% | {residual:8.3f}% at {offset!s:>10}             | {mask_diff:8.3f}%")

    diff0 = percent(np.count_nonzero(changed(key, cells[0])), SIZE * SIZE)
    diff1 = percent(np.count_nonzero(changed(key, cells[1])), SIZE * SIZE)
    cell1_vs_cell2 = percent(np.count_nonzero(changed(cells[1], cells[2])), SIZE * SIZE)
    print(f"\ncell 1 vs cell 2 diff (>60 summed RGB): {cell1_vs_cell2:.3f}%")
    checks = [
        diff0 >= 0.4,
        diff1 >= 0.8,
        cell1_vs_cell2 > 0.0,
        all(78.0 <= m[0] <= 92.0 and 96.0 <= m[1] <= 102.0 and m[2] >= 4.0 and 2.0 <= m[3] < 20.0 for m in bank_metrics),
        np.all(cells[5] == GREEN),
    ]
    if not all(checks):
        raise SystemExit("FAIL: one or more J1b constraints did not pass")
    print("\nPASS: J1b dimensions, state differences, bank envelopes, residuals, and empty cell pass")


if __name__ == "__main__":
    main()
