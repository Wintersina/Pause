from pathlib import Path

import colorsys
import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
MAGENTA = np.array((255, 0, 255), dtype=np.uint8)
SIZE = 1024
THRESHOLD = 60


def load(path, size):
    image = Image.open(path).convert("RGB")
    if image.size != size:
        raise AssertionError(f"{path.name} is {image.size}, expected {size}")
    return image


def cell(sheet, index):
    x = (index % 3) * SIZE
    y = (index // 3) * SIZE
    return sheet.crop((x, y, x + SIZE, y + SIZE))


def array(image):
    return np.asarray(image, dtype=np.int16)


def changed_map(a, b):
    return np.abs(array(a) - array(b)).sum(axis=2) >= THRESHOLD


def changed_percent(a, b, allowed=None):
    changed = changed_map(a, b)
    if allowed is not None:
        changed = changed & allowed
        total = int(np.count_nonzero(allowed))
    else:
        total = SIZE * SIZE
    return 100.0 * int(np.count_nonzero(changed)) / total if total else 0.0


def silhouette(image):
    return np.any(np.asarray(image, dtype=np.uint8) != MAGENTA, axis=2)


def bbox(mask):
    ys, xs = np.where(mask)
    return (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)


def silhouette_difference(a, b):
    return 100.0 * float(np.count_nonzero(silhouette(a) != silhouette(b))) / (SIZE * SIZE)


def best_translation_residual(key, bank, radius=64):
    kp = array(key)
    bp = array(bank)
    best = 100.0
    best_offset = (0, 0)
    coarse = []
    for dy in range(-radius, radius + 1, 4):
        for dx in range(-radius, radius + 1, 4):
            coarse.append((dx, dy))

    def score(dx, dy):
        y0 = max(0, -dy)
        y1 = min(SIZE, SIZE - dy)
        x0 = max(0, -dx)
        x1 = min(SIZE, SIZE - dx)
        overlap = np.abs(kp[y0:y1, x0:x1] - bp[y0 + dy:y1 + dy, x0 + dx:x1 + dx]).sum(axis=2)
        changed = int(np.count_nonzero(overlap >= THRESHOLD))
        changed += SIZE * SIZE - (y1 - y0) * (x1 - x0)
        return 100.0 * changed / (SIZE * SIZE)

    for dx, dy in coarse:
        value = score(dx, dy)
        if value < best:
            best = value
            best_offset = (dx, dy)

    coarse_x, coarse_y = best_offset
    for dy in range(max(-radius, coarse_y - 3), min(radius, coarse_y + 3) + 1):
        for dx in range(max(-radius, coarse_x - 3), min(radius, coarse_x + 3) + 1):
            value = score(dx, dy)
            if value < best:
                best = value
                best_offset = (dx, dy)
    return best, best_offset


def canopy_mask(key):
    pixels = np.asarray(key, dtype=np.int16)
    mask = np.zeros((SIZE, SIZE), dtype=bool)
    region = pixels[350:670, 385:640]
    teal = (region[:, :, 2] > 90) & (region[:, :, 1] > 80)
    teal &= region[:, :, 2] > region[:, :, 0] + 20
    teal &= region[:, :, 1] > region[:, :, 0] + 10
    mask[350:670, 385:640] = teal
    return mask


def direct_union_difference(a, b):
    union = silhouette(a) | silhouette(b)
    changed = changed_map(a, b) & union
    return 100.0 * float(np.count_nonzero(changed)) / int(np.count_nonzero(union))


def magenta_like_ship_pixels(image):
    count = 0
    for r, g, b in np.asarray(image, dtype=np.uint8).reshape(-1, 3):
        if (r, g, b) == (255, 0, 255):
            continue
        h, s, _ = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
        if s > 0.5 and 280.0 / 360.0 <= h <= 345.0 / 360.0:
            count += 1
    return count


def main():
    key = load(ROOT / "key_state0.png", (SIZE, SIZE))
    sheet = load(ROOT / "hull_redo_state0.png", (SIZE * 3, SIZE * 2))
    cells = [cell(sheet, i) for i in range(5)]
    empty = cell(sheet, 5)
    canopy = canopy_mask(key)
    diffs = [changed_percent(key, image) for image in cells]
    canopy1 = changed_percent(key, cells[1], canopy)
    canopy2 = changed_percent(cells[1], cells[2], canopy)
    cell2_vs_cell1 = changed_percent(cells[1], cells[2])

    print("J1b state0 pixel-difference table")
    print("cell       vs key       canopy metric")
    print(f"0          {diffs[0]:7.2f}%      lights-off vs key")
    print(f"1          {diffs[1]:7.2f}%      canopy vs key: {canopy1:7.2f}%")
    print(f"2          {diffs[2]:7.2f}%      canopy vs cell1: {canopy2:7.2f}%")
    print(f"3          {diffs[3]:7.2f}%      bank-left vs key")
    print(f"4          {diffs[4]:7.2f}%      bank-right vs key")
    print(f"cell2 vs cell1: {cell2_vs_cell1:7.2f}%")

    key_box = bbox(silhouette(key))
    key_w = key_box[2] - key_box[0]
    key_h = key_box[3] - key_box[1]
    bank_rows = []
    for name, bank in (("left", cells[3]), ("right", cells[4])):
        residual, offset = best_translation_residual(key, bank)
        sil_diff = silhouette_difference(key, bank)
        bank_box = bbox(silhouette(bank))
        bank_w = bank_box[2] - bank_box[0]
        bank_h = bank_box[3] - bank_box[1]
        width_pct = 100.0 * bank_w / key_w
        height_pct = 100.0 * bank_h / key_h
        bank_rows.append((name, residual, sil_diff, width_pct, height_pct, offset))

    direct = direct_union_difference(cells[3], cells[4])
    print("\nbank audit")
    print("bank        residual   silhouette   bbox width   bbox height   best translation")
    for name, residual, sil_diff, width_pct, height_pct, offset in bank_rows:
        print(f"{name:5s}       {residual:7.2f}%    {sil_diff:7.2f}%      {width_pct:7.2f}%      {height_pct:7.2f}%       {offset}")
    print(f"left/right direct union difference: {direct:7.2f}%")

    magenta_counts = [magenta_like_ship_pixels(image) for image in cells]
    print(f"magenta-like ship pixels: {magenta_counts}")

    assert diffs[0] >= 0.4, "cell 0 < 0.4%"
    assert diffs[1] >= 0.8, "cell 1 < 0.8%"
    assert changed_percent(cells[1], cells[2]) > 0.0, "cell 2 did not differ from cell 1"
    assert canopy1 > 0.0, "cell 1 canopy did not change"
    assert canopy2 > 0.0, "cell 2 canopy did not change from cell 1"
    for name, residual, sil_diff, width_pct, height_pct, _ in bank_rows:
        assert residual >= 5.0, f"{name} residual < 5%"
        assert sil_diff >= 3.0, f"{name} silhouette difference < 3%"
        assert 78.0 <= width_pct <= 94.0, f"{name} bbox width outside 78-94%"
        assert 97.0 <= height_pct <= 103.0, f"{name} bbox height outside 3%"
    assert direct >= 8.0, "left/right direct union difference < 8%"
    assert all(value == 0 for value in magenta_counts), "magenta-like ship pixels found"
    assert np.all(np.asarray(empty, dtype=np.uint8) == MAGENTA), "cell 5 is not completely empty magenta"
    print("\nPASS")


if __name__ == "__main__":
    main()
