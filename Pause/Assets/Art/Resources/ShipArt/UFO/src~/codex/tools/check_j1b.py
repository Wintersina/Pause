#!/usr/bin/env python3
"""Validate J1b sheets against the immutable UFO keys."""

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
SIZE = 1024
GREEN = np.array((0, 255, 0), dtype=np.uint8)


def canopy_mask() -> np.ndarray:
    m = Image.new("1", (SIZE, SIZE), 0)
    ImageDraw.Draw(m).polygon(
        [(512, 418), (590, 464), (590, 560),
         (512, 606), (434, 560), (434, 464)],
        fill=1,
    )
    return np.array(m, dtype=bool)


def rgb(path: Path) -> np.ndarray:
    return np.array(Image.open(path).convert("RGB"))


def cells(sheet: np.ndarray) -> list[np.ndarray]:
    return [
        sheet[0:SIZE, i * SIZE:(i + 1) * SIZE]
        for i in range(3)
    ]


def diff_mask(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    return np.abs(a.astype(np.int16) - b.astype(np.int16)).sum(axis=2) > 60


def main() -> int:
    canopy = canopy_mask()
    print("file                 cell0 diff   cell1 diff   cell2 diff   cell1 canopy   cell2 canopy   damage mismatches   silhouette mismatches")
    print("-------------------  -----------  -----------  -----------  -------------  -------------  -----------------  ---------------------")
    all_ok = True
    for state in range(3):
        key = rgb(ROOT / f"key_state{state}.png")
        sheet = rgb(ROOT / f"hull_redo_state{state}.png")
        out_cells = cells(sheet)
        diff_pcts = []
        sil_mismatches = []
        damage_mismatches = []
        for cell in out_cells:
            diff_pcts.append(100.0 * diff_mask(cell, key).mean())
            key_sil = np.any(key != GREEN, axis=2)
            out_sil = np.any(cell != GREEN, axis=2)
            sil_mismatches.append(int(np.count_nonzero(key_sil != out_sil)))
            if state == 0:
                damage_mismatches.append(0)
            else:
                damage = diff_mask(key, rgb(ROOT / "key_state0.png"))
                damage_mismatches.append(int(np.count_nonzero(np.any(cell[damage] != key[damage], axis=1))))
        canopy_pcts = [100.0 * diff_mask(out_cells[i], key)[canopy].mean() for i in (1, 2)]
        cell2_vs_cell1 = 100.0 * diff_mask(out_cells[2], out_cells[1]).mean()
        print(
            f"state{state}               {diff_pcts[0]:8.3f}%    {diff_pcts[1]:8.3f}%    {diff_pcts[2]:8.3f}%    "
            f"{canopy_pcts[0]:11.3f}%  {canopy_pcts[1]:11.3f}%  {sum(damage_mismatches):17d}  {sum(sil_mismatches):21d}"
        )
        ok = (
            diff_pcts[0] >= 0.4
            and diff_pcts[1] >= 0.8
            and cell2_vs_cell1 > 0.0
            and all(v == 0 for v in damage_mismatches)
            and all(v == 0 for v in sil_mismatches)
        )
        if not ok:
            all_ok = False
        print(f"  cell2-vs-cell1 diff: {cell2_vs_cell1:.3f}% | damage per cell: {damage_mismatches} | silhouette per cell: {sil_mismatches}")
    return 0 if all_ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
