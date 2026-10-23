from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
SIZE = 1024
GREEN = (0, 255, 0)
DIFF_THRESHOLD = 60


def load_cell(sheet, index):
    x = (index % 3) * SIZE
    y = (index // 3) * SIZE
    return sheet.crop((x, y, x + SIZE, y + SIZE)).convert("RGB")


def load_sheet():
    path = ROOT / "hull_redo_state0.png"
    image = Image.open(path).convert("RGB")
    if image.size != (SIZE * 3, SIZE * 2):
        raise ValueError(f"hull_redo_state0.png: expected 3072x2048, got {image.size}")
    return image


def green(pixel):
    return pixel == GREEN


def changed(a, b):
    return sum(abs(x - y) for x, y in zip(a, b)) > DIFF_THRESHOLD


def diff_percent(base, other):
    a = np.asarray(base, dtype=np.int16)
    b = np.asarray(other, dtype=np.int16)
    count = np.count_nonzero(np.abs(a - b).sum(axis=2) > DIFF_THRESHOLD)
    return 100.0 * count / (SIZE * SIZE)


def bbox_and_area(image):
    pixels = image.load()
    points = [(x, y) for y in range(SIZE) for x in range(SIZE) if not green(pixels[x, y])]
    if not points:
        return None, 0
    return (
        min(x for x, _ in points),
        min(y for _, y in points),
        max(x for x, _ in points),
        max(y for _, y in points),
    ), len(points)


def canopy_mask():
    mask = Image.new("1", (SIZE, SIZE), 0)
    draw = ImageDraw.Draw(mask)
    draw.polygon(
        [(479, 177), (546, 177), (595, 239), (595, 397), (543, 460),
         (481, 460), (428, 397), (428, 239)],
        fill=1,
    )
    return mask


def canopy_diff_percent(base, other, mask):
    bp = base.load()
    op = other.load()
    changed_count = 0
    total = 0
    for y in range(SIZE):
        for x in range(SIZE):
            if mask.getpixel((x, y)):
                total += 1
                if changed(bp[x, y], op[x, y]):
                    changed_count += 1
    return 100.0 * changed_count / max(1, total)


def translated_residual_percent(base, other, radius=20):
    bp = np.asarray(base, dtype=np.int16)
    op = np.asarray(other, dtype=np.int16)
    best = 100.0
    best_shift = (0, 0)
    for dy in range(-radius, radius + 1):
        for dx in range(-radius, radius + 1):
            shifted = np.full_like(bp, GREEN, dtype=np.int16)
            x0 = max(0, dx)
            x1 = min(SIZE, SIZE + dx)
            y0 = max(0, dy)
            y1 = min(SIZE, SIZE + dy)
            shifted[y0:y1, x0:x1] = bp[y0 - dy:y1 - dy, x0 - dx:x1 - dx]
            changed_count = np.count_nonzero(np.abs(shifted - op).sum(axis=2) > DIFF_THRESHOLD)
            percent = 100.0 * changed_count / (SIZE * SIZE)
            if percent < best:
                best = percent
                best_shift = (dx, dy)
    return best, best_shift


def main():
    sheet = load_sheet()
    key = Image.open(ROOT / "key_state0.png").convert("RGB")
    cells = [load_cell(sheet, i) for i in range(6)]
    if np.any(np.asarray(cells[5]) != np.asarray(GREEN)):
        raise AssertionError("cell 5 is not completely empty")

    mask = canopy_mask()
    print("cell  diff-vs-key  canopy-diff  bbox                  area       residual")
    results = []
    for i in range(5):
        diff = diff_percent(key, cells[i])
        canopy_diff = canopy_diff_percent(key, cells[i], mask) if i in (1, 2) else None
        bbox, area = bbox_and_area(cells[i])
        residual = "-"
        if i in (3, 4):
            key_bbox, key_area = bbox_and_area(key)
            width = bbox[2] - bbox[0] + 1
            key_width = key_bbox[2] - key_bbox[0] + 1
            width_change = 100.0 * abs(width - key_width) / key_width
            area_change = 100.0 * abs(area - key_area) / key_area
            residual_value, shift = translated_residual_percent(key, cells[i])
            residual = f"{residual_value:6.3f}% @ {shift[0]:+d},{shift[1]:+d}"
            results.append((i, width_change, area_change, residual_value))
        print(f"{i:>4}  {diff:10.3f}%  {canopy_diff if canopy_diff is not None else '-':>11}  {bbox!s:23} {area:8d}  {residual}")

    c0 = diff_percent(key, cells[0])
    c1 = diff_percent(key, cells[1])
    c2_vs_1 = diff_percent(cells[1], cells[2])
    print(f"\ncell 0 minimum: {c0:.3f}% (required >= 0.400%)")
    print(f"cell 1 minimum: {c1:.3f}% (required >= 0.800%)")
    print(f"cell 2 vs cell 1: {c2_vs_1:.3f}% (required > 0%)")
    for i, width_change, area_change, residual in results:
        print(f"bank {i}: width change {width_change:.3f}% (4-22%), area change {area_change:.3f}% (<25%), residual {residual:.3f}% (>=4%)")

    ok = (
        c0 >= 0.4 and c1 >= 0.8 and c2_vs_1 > 0 and
        all(4.0 <= width <= 22.0 and area < 25.0 and residual >= 4.0
            for _, width, area, residual in results)
    )
    print(f"\nPASS: {ok}")
    if not ok:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
