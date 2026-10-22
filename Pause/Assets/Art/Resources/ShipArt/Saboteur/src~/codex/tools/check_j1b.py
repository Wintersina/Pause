#!/usr/bin/env python3
"""Audit J1b pose sheets and print the requested per-cell metrics."""

from math import atan2, degrees
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)
SIZE = 1024
FILES = [ROOT / f"hull_redo_state{i}.png" for i in range(3)]
CORE_BOX = (444, 454, 620, 686)


def diff(a, b, threshold=60):
    aa = np.asarray(a, dtype=np.int16)
    bb = np.asarray(b, dtype=np.int16)
    return int(np.sum(np.sum(np.abs(aa - bb), axis=2) > threshold))


def mask(im):
    return {(x, y) for y in range(SIZE) for x in range(SIZE) if im.getpixel((x, y)) != GREEN}


def bbox(mask_set):
    xs = [p[0] for p in mask_set]
    ys = [p[1] for p in mask_set]
    return min(xs), min(ys), max(xs) + 1, max(ys) + 1


def core_mask(im):
    x0, y0, x1, y1 = CORE_BOX
    out = set()
    for y in range(y0, y1):
        for x in range(x0, x1):
            r, g, b = im.getpixel((x, y))
            if b > 115 and g > 95 and b > r + 40 and g > r + 30:
                out.add((x, y))
    return out


def shifted_diff(key, bank, tx, ty):
    aa = np.asarray(key, dtype=np.int16)
    bb = np.asarray(bank, dtype=np.int16)
    count = 0
    ax0, ax1 = max(0, -tx), min(SIZE, SIZE - tx)
    ay0, ay1 = max(0, -ty), min(SIZE, SIZE - ty)
    bx0, bx1 = max(0, tx), min(SIZE, SIZE + tx)
    by0, by1 = max(0, ty), min(SIZE, SIZE + ty)
    if ax1 > ax0 and ay1 > ay0:
        count += int(np.sum(np.sum(np.abs(aa[ay0:ay1, ax0:ax1] - bb[by0:by1, bx0:bx1]), axis=2) > 60))
    outside = np.any(bb != np.asarray(GREEN, dtype=np.int16), axis=2)
    inside = np.zeros((SIZE, SIZE), dtype=bool)
    inside[by0:by1, bx0:bx1] = True
    count += int(np.sum(outside & ~inside))
    return count


def best_translation(key, bank):
    best = None
    # The builder centers every bank; this local exhaustive window covers the possible
    # integer anchor correction without making the audit needlessly expensive.
    for ty in range(-8, 9):
        for tx in range(-8, 9):
            value = shifted_diff(key, bank, tx, ty)
            if best is None or value < best[0]:
                best = value, tx, ty
    return best


def lean_angle(sil):
    ys = [p[1] for p in sil]
    top_cut = sorted(ys)[max(1, len(ys) // 20)]
    bottom_cut = sorted(ys)[-max(1, len(ys) // 20)]
    top = [p for p in sil if p[1] <= top_cut]
    bottom = [p for p in sil if p[1] >= bottom_cut]
    top_x = sum(p[0] for p in top) / len(top)
    top_y = sum(p[1] for p in top) / len(top)
    bottom_x = sum(p[0] for p in bottom) / len(bottom)
    bottom_y = sum(p[1] for p in bottom) / len(bottom)
    return degrees(atan2(top_x - bottom_x, bottom_y - top_y))


def cell(sheet, index):
    return sheet.crop(((index % 3) * SIZE, (index // 3) * SIZE,
                       ((index % 3) + 1) * SIZE, ((index // 3) + 1) * SIZE))


def pct(n, d=SIZE * SIZE):
    return n * 100.0 / d


def main():
    sheets = [Image.open(path).convert("RGB") for path in FILES]
    for path, sheet in zip(FILES, sheets):
        if sheet.size != (SIZE * 3, SIZE * 2):
            raise SystemExit(f"FAIL {path.name}: expected 3072x2048, got {sheet.size}")

    print("J1b audit: thresholded RGB difference threshold=60; all percentages are per 1024x1024 cell")
    print("file                 c0     c1     c2     c3     c4  c2-c1  core1  core2   bank   silΔ   bboxW   lean°")
    print("-------------------  -----  -----  -----  -----  -----  ------  ------  ------  ------  ------  ------  ------")
    for path, sheet in zip(FILES, sheets):
        key = Image.open(ROOT / f"key_state{FILES.index(path)}.png").convert("RGB")
        cells = [cell(sheet, i) for i in range(6)]
        values = [pct(diff(key, cells[i])) for i in range(5)]
        cell2_from_cell1 = pct(diff(cells[1], cells[2]))
        key_core = core_mask(key)
        core1 = pct(sum(sum(abs(a - b) for a, b in zip(key.getpixel(p), cells[1].getpixel(p))) > 60
                        for p in key_core), len(key_core))
        core2 = pct(sum(sum(abs(a - b) for a, b in zip(key.getpixel(p), cells[2].getpixel(p))) > 60
                        for p in key_core), len(key_core))
        bank_metrics = []
        for index in (3, 4):
            bank = cells[index]
            residual, tx, ty = best_translation(key, bank)
            bank_sil = mask(bank)
            key_sil = mask(key)
            sil_delta = len(key_sil ^ bank_sil) * 100.0 / len(key_sil)
            key_box = bbox(key_sil)
            bank_box = bbox(bank_sil)
            width_ratio = (bank_box[2] - bank_box[0]) * 100.0 / (key_box[2] - key_box[0])
            angle = lean_angle(bank_sil)
            bank_metrics.append((pct(residual), sil_delta, width_ratio, angle, tx, ty))
        residual = max(m[0] for m in bank_metrics)
        sil_delta = max(m[1] for m in bank_metrics)
        width_ratio = max(m[2] for m in bank_metrics)
        angle = bank_metrics[0][3]
        print(f"{path.name:19}  {values[0]:5.2f}  {values[1]:5.2f}  {values[2]:5.2f}  {values[3]:5.2f}  {values[4]:5.2f}  {cell2_from_cell1:6.2f}  {core1:6.2f}  {core2:6.2f}  {residual:6.2f}  {sil_delta:6.2f}  {width_ratio:6.2f}  {angle:6.2f}")
        for side, metric in zip(("LEFT", "RIGHT"), bank_metrics):
            residual, sil_delta, width_ratio, angle, tx, ty = metric
            print(f"  bank {side:5}: residual={residual:.2f}% silhouetteΔ={sil_delta:.2f}% bboxW={width_ratio:.2f}% lean={angle:.2f}° best_translation=({tx:+d},{ty:+d})")

        assert values[0] >= 0.4, f"{path.name} cell0 diff below 0.4%"
        assert values[1] >= 0.8, f"{path.name} cell1 diff below 0.8%"
        assert cell2_from_cell1 > 0.0, f"{path.name} cell2 does not differ from cell1"
        assert all(cells[5].getpixel((x, y)) == GREEN for y in range(SIZE) for x in range(SIZE)), \
            f"{path.name} cell5 is not empty"
        for side, metric in zip(("LEFT", "RIGHT"), bank_metrics):
            residual, sil_delta, width_ratio, angle, _, _ = metric
            assert residual >= 5.0, f"{path.name} {side} bank residual below 5%"
            assert 3.0 <= sil_delta <= 22.0, f"{path.name} {side} silhouette delta outside 3-22%"
            assert 80.0 <= width_ratio <= 100.0, f"{path.name} {side} bbox width outside 80-100%"
            assert width_ratio <= 100.0, f"{path.name} {side} bank wider than key"
    print("PASS")


if __name__ == "__main__":
    main()
