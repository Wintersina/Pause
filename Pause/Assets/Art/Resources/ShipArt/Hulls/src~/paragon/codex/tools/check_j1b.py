"""Measure J1b against the approved, attached 1024 px reference key poses."""

from pathlib import Path

import numpy as np
from PIL import Image


HERE = Path(__file__).resolve().parent
OUT = HERE.parent
GREEN = np.array((0, 255, 0), dtype=np.uint8)
CELL = 1024


def shift(rgb, dx, dy):
    out = np.empty_like(rgb)
    out[:] = GREEN
    sx0, sx1 = max(0, -dx), min(CELL, CELL - dx)
    sy0, sy1 = max(0, -dy), min(CELL, CELL - dy)
    out[sy0 + dy:sy1 + dy, sx0 + dx:sx1 + dx] = rgb[sy0:sy1, sx0:sx1]
    return out


def changed(a, b):
    return np.abs(a.astype(np.int16) - b.astype(np.int16)).sum(axis=2) > 60


def pct(mask):
    return 100.0 * float(np.count_nonzero(mask)) / mask.size


def best_shift(ref, frame):
    """Search integer offsets at 4 px resolution, then every nearby pixel."""
    best = (float("inf"), 0, 0)
    ref_small = ref[::4, ::4]
    for dy in range(-40, 41, 4):
        for dx in range(-40, 41, 4):
            score = pct(changed(shift(frame, dx, dy)[::4, ::4], ref_small))
            if score < best[0]:
                best = (score, dx, dy)
    bx, by = best[1:]
    best = (float("inf"), bx, by)
    for dy in range(by - 4, by + 5):
        for dx in range(bx - 4, bx + 5):
            score = pct(changed(shift(frame, dx, dy), ref))
            if score < best[0]:
                best = (score, dx, dy)
    return best


def canopy(ref):
    y, x = np.indices((CELL, CELL))
    v = ref.astype(np.int16)
    glass = (((x - 512) / 68) ** 2 + ((y - 420) / 123) ** 2 < 1.0)
    blue = (v[:, :, 2] > v[:, :, 0] * 1.35) & (v[:, :, 1] > v[:, :, 0] * 1.1)
    return glass & blue & (v[:, :, 2] > 65)


def main():
    print("All-pixel changes use summed RGB difference > 60; percentages use 1024² pixels.")
    print("state cell pose        diff%  canopy%  residual%  silhouette%  best shift")
    failures = []
    for state in range(3):
        ref = np.asarray(Image.open(HERE / f"j1b_ref_state{state}.png").convert("RGB"))
        path = OUT / f"hull_redo_state{state}.png"
        sheet = np.asarray(Image.open(path).convert("RGB"))
        if sheet.shape != (CELL * 2, CELL * 3, 3):
            raise ValueError(f"{path}: expected 3072x2048 pixels")
        cells = [sheet[(i // 3) * CELL:(i // 3 + 1) * CELL,
                       (i % 3) * CELL:(i % 3 + 1) * CELL] for i in range(6)]
        if not np.all(cells[5] == GREEN):
            failures.append(f"state {state}: cell 5 is not pure green")
        glass = canopy(ref)
        for i, pose in enumerate(("lights off", "glint 1", "glint 2", "bank left", "bank right")):
            cell = cells[i]
            diff = pct(changed(cell, ref))
            glass_diff = 100 * np.count_nonzero(changed(cell, ref) & glass) / np.count_nonzero(glass) if i in (1, 2) else None
            residual = silhouette = None
            offset = "—"
            if i in (3, 4):
                residual, dx, dy = best_shift(ref, cell)
                aligned = shift(cell, dx, dy)
                silhouette = pct(np.any(aligned != GREEN, axis=2) ^ np.any(ref != GREEN, axis=2))
                offset = f"({dx:+d},{dy:+d})"
                if residual < 5:
                    failures.append(f"state {state} cell {i}: residual {residual:.2f}% < 5%")
                if silhouette < 3:
                    failures.append(f"state {state} cell {i}: silhouette {silhouette:.2f}% < 3%")
            print(f"  {state}     {i}  {pose:<10} {diff:6.2f}   "
                  f"{f'{glass_diff:.2f}' if glass_diff is not None else '—':>6}   "
                  f"{f'{residual:.2f}' if residual is not None else '—':>8}   "
                  f"{f'{silhouette:.2f}' if silhouette is not None else '—':>10}   {offset}")
            if i == 0 and diff < 0.4:
                failures.append(f"state {state} cell 0: {diff:.2f}% < 0.4%")
            if i == 1 and diff < 0.8:
                failures.append(f"state {state} cell 1: {diff:.2f}% < 0.8%")
        if not np.any(changed(cells[2], cells[1])):
            failures.append(f"state {state}: glint 2 equals glint 1")
    if failures:
        raise SystemExit("FAIL:\n" + "\n".join(failures))
    print("PASS: all J1b numeric thresholds and empty cells")


if __name__ == "__main__":
    main()
