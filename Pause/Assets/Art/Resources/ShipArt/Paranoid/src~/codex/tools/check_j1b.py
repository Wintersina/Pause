from pathlib import Path
import colorsys

import numpy as np
from PIL import Image, ImageChops


ROOT = Path(__file__).resolve().parents[1]
MAGENTA = (255, 0, 255)
SIZE = 1024
SHEET_SIZE = (SIZE * 3, SIZE * 2)
THRESHOLD = 60


def load_key(state):
    image = Image.open(ROOT / f"key_state{state}.png").convert("RGB")
    if image.size != (SIZE, SIZE):
        raise AssertionError(f"key_state{state}.png is not 1024x1024")
    return image


def load_sheet(state):
    image = Image.open(ROOT / f"hull_redo_state{state}.png").convert("RGB")
    if image.size != SHEET_SIZE:
        raise AssertionError(f"hull_redo_state{state}.png is not 3072x2048")
    return image


def cell(sheet, index):
    x = (index % 3) * SIZE
    y = (index // 3) * SIZE
    return sheet.crop((x, y, x + SIZE, y + SIZE))


def rgb_delta(a, b):
    return sum(abs(a[i] - b[i]) for i in range(3))


def changed_percent(a, b, allowed=None):
    aa = np.asarray(a, dtype=np.int16)
    bb = np.asarray(b, dtype=np.int16)
    changed_map = np.abs(aa - bb).sum(axis=2) >= THRESHOLD
    if allowed is None:
        changed = int(np.count_nonzero(changed_map))
        total = SIZE * SIZE
    else:
        changed = int(np.count_nonzero(changed_map & allowed))
        total = int(np.count_nonzero(allowed))
    return 100.0 * changed / total if total else 0.0


def silhouette(image):
    return np.any(np.asarray(image) != np.asarray(MAGENTA, dtype=np.uint8), axis=2)


def silhouette_difference(a, b):
    am = silhouette(a)
    bm = silhouette(b)
    return 100.0 * float(np.count_nonzero(am != bm)) / (SIZE * SIZE)


def best_translation_residual(key, bank, radius=64):
    """Minimum changed-pixel percentage over integer translations of the key."""
    kp = np.asarray(key, dtype=np.int16)
    bp = np.asarray(bank, dtype=np.int16)
    best = 100.0
    best_offset = (0, 0)
    coarse = []
    for dy in range(-radius, radius + 1, 4):
        for dx in range(-radius, radius + 1, 4):
            coarse.append((dx, dy))
    best_coarse = (0, 0)
    best_coarse_value = 100.0
    for dx, dy in coarse:
        y0 = max(0, -dy)
        y1 = min(SIZE, SIZE - dy)
        x0 = max(0, -dx)
        x1 = min(SIZE, SIZE - dx)
        overlap = np.abs(kp[y0:y1, x0:x1] - bp[y0 + dy:y1 + dy, x0 + dx:x1 + dx]).sum(axis=2)
        changed = int(np.count_nonzero(overlap >= THRESHOLD))
        changed += SIZE * SIZE - (y1 - y0) * (x1 - x0)
        value = 100.0 * changed / (SIZE * SIZE)
        if value < best_coarse_value:
            best_coarse_value = value
            best_coarse = (dx, dy)
    cx, cy = best_coarse
    for dy in range(max(-radius, cy - 3), min(radius, cy + 3) + 1):
        for dx in range(max(-radius, cx - 3), min(radius, cx + 3) + 1):
            y0 = max(0, -dy)
            y1 = min(SIZE, SIZE - dy)
            x0 = max(0, -dx)
            x1 = min(SIZE, SIZE - dx)
            if y0 >= y1 or x0 >= x1:
                continue
            overlap = np.abs(kp[y0:y1, x0:x1] - bp[y0 + dy:y1 + dy, x0 + dx:x1 + dx]).sum(axis=2)
            changed = int(np.count_nonzero(overlap >= THRESHOLD))
            changed += SIZE * SIZE - (y1 - y0) * (x1 - x0)
            value = 100.0 * changed / (SIZE * SIZE)
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
    am = silhouette(a)
    bm = silhouette(b)
    aa = np.asarray(a, dtype=np.int16)
    bb = np.asarray(b, dtype=np.int16)
    union = am | bm
    changed_map = np.abs(aa - bb).sum(axis=2) >= THRESHOLD
    changed = int(np.count_nonzero(changed_map & union))
    total = int(np.count_nonzero(union))
    return 100.0 * changed / total if total else 0.0


def magenta_like_ship_pixels(image):
    count = 0
    for pixel in image.getdata():
        if pixel == MAGENTA:
            continue
        r, g, b = pixel
        h, s, _ = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
        if s > 0.5 and 280.0 / 360.0 <= h <= 345.0 / 360.0:
            count += 1
    return count


def main():
    keys = [load_key(i) for i in range(3)]
    sheets = [load_sheet(i) for i in range(3)]
    print("file                 cell0    cell1    cell2    cell3    cell4   canopy1 canopy2")
    print("                     vs key  vs key  vs key  vs key  vs key   vs key  vs cell1")
    all_rows = []
    for state, sheet in enumerate(sheets):
        key = keys[state]
        cells = [cell(sheet, i) for i in range(5)]
        values = [changed_percent(key, c) for c in cells]
        canopy = canopy_mask(key)
        canopy1 = changed_percent(key, cells[1], canopy)
        canopy2 = changed_percent(cells[1], cells[2], canopy)
        all_rows.append((values, canopy1, canopy2))
        print(
            f"state{state}               {values[0]:6.2f}%  {values[1]:6.2f}%  {values[2]:6.2f}%  "
            f"{values[3]:6.2f}%  {values[4]:6.2f}%   {canopy1:6.2f}%  {canopy2:6.2f}%"
        )

    print("\nbanks: residual after best integer translation / silhouette difference")
    bank_rows = []
    for state, (key, sheet) in enumerate(zip(keys, sheets)):
        left = cell(sheet, 3)
        right = cell(sheet, 4)
        residual, offset = best_translation_residual(key, left)
        silhouette_diff = silhouette_difference(key, left)
        direct = direct_union_difference(left, right)
        flip_diff = changed_percent(left.transpose(Image.Transpose.FLIP_LEFT_RIGHT), right)
        bank_rows.append((residual, silhouette_diff, direct, flip_diff, offset))
        print(
            f"state{state} left residual {residual:6.2f}% @ {offset!s:>10} | "
            f"silhouette {silhouette_diff:6.2f}% | left/right {direct:6.2f}% | "
            f"right-vs-flip {flip_diff:6.2f}%"
        )

    print("\nmagenta-like non-background ship pixels")
    for state, sheet in enumerate(sheets):
        counts = [magenta_like_ship_pixels(cell(sheet, i)) for i in range(5)]
        print(f"state{state}: {counts}")

    assert all(all(v >= 0.0 for v in row[0]) for row in all_rows)
    for state, (values, canopy1, canopy2) in enumerate(all_rows):
        assert values[0] >= 0.4, f"state{state} cell0 < 0.4%"
        assert values[1] >= 0.8, f"state{state} cell1 < 0.8%"
        assert values[2] != values[1] or canopy2 > 0.0, f"state{state} cell2 did not differ from cell1"
        assert canopy1 > 0.0, f"state{state} cell1 canopy did not change"
        assert canopy2 > 0.0, f"state{state} cell2 canopy did not change from cell1"
    for state, (residual, silhouette_diff, direct, flip_diff, _) in enumerate(bank_rows):
        assert residual >= 8.0, f"state{state} bank residual < 8%"
        assert silhouette_diff >= 5.0, f"state{state} bank silhouette < 5%"
        assert direct >= 8.0, f"state{state} bank left/right < 8%"
        assert flip_diff > 0.0, f"state{state} bank right is pixel-exact horizontal flip"
    assert all(magenta_like_ship_pixels(cell(sheet, i)) == 0 for sheet in sheets for i in range(5))
    print("\nPASS")


if __name__ == "__main__":
    main()
