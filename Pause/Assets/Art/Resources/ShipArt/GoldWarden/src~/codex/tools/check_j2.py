#!/usr/bin/env python3
"""Validate J2 flame sheets and render a review preview."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
CRUISE = ROOT / "flame_cruise.png"
BOOST = ROOT / "flame_boost.png"
PREVIEW = ROOT / "preview_j2.png"

GREEN = (0, 255, 0)
COLORS = {
    (126, 78, 12): "brown",
    (217, 154, 26): "amber",
    (255, 211, 106): "light_gold",
    (244, 234, 212): "bone_white",
    (216, 35, 44): "red",
    GREEN: "green",
}


def read_sheet(path: Path) -> np.ndarray:
    image = Image.open(path).convert("RGB")
    array = np.asarray(image, dtype=np.uint8)
    if array.shape != (1024, 1536, 3):
        raise AssertionError(f"{path.name}: expected 1536x1024 RGB, got {image.size}")
    return array


def report(name: str, array: np.ndarray) -> dict[str, object]:
    palette = {tuple(rgb): int(np.sum(np.all(array == rgb, axis=2))) for rgb in COLORS}
    allowed = np.array(list(COLORS), dtype=np.uint8)
    matches = np.any(
        np.all(array[:, :, None, :] == allowed[None, None, :, :], axis=3),
        axis=2,
    )
    unknown = ~matches
    if np.any(unknown):
        raise AssertionError(f"{name}: contains {int(np.sum(unknown))} pixels outside the allowed palette")

    cell_reports = []
    for index in range(6):
        cell = array[:, index * 256:(index + 1) * 256]
        border = np.concatenate((cell[0, :, :], cell[-1, :, :], cell[:, 0, :], cell[:, -1, :]), axis=0)
        if not np.all(border == np.array(GREEN, dtype=np.uint8)):
            raise AssertionError(f"{name}: cell {index} border is not pure #00FF00")
        mask = np.any(cell != np.array(GREEN, dtype=np.uint8), axis=2)
        ys, xs = np.where(mask)
        if len(xs) == 0:
            raise AssertionError(f"{name}: cell {index} is empty")
        counts = {key: int(np.sum(np.all(cell == key, axis=2))) for key in COLORS if key != GREEN}
        cell_reports.append(
            {
                "index": index,
                "bbox": (int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())),
                "height": int(ys.max() - ys.min() + 1),
                "width": int(xs.max() - xs.min() + 1),
                "bone_white": counts[(244, 234, 212)],
                "sparks": counts[(216, 35, 44)],
            }
        )

    return {"name": name, "pixels": palette, "cells": cell_reports}


def make_preview(cruise: np.ndarray, boost: np.ndarray) -> None:
    bg = np.zeros((420, 640, 3), dtype=np.uint8)
    bg[:, :] = (11, 11, 26)
    for row, sheet in enumerate((cruise, boost)):
        # Show each sheet as six compact columns; green key pixels become the dark review background.
        small = Image.fromarray(sheet, mode="RGB").resize((640, 213), Image.Resampling.NEAREST)
        small_array = np.asarray(small, dtype=np.uint8).copy()
        small_array[np.all(small_array == np.array(GREEN, dtype=np.uint8), axis=2)] = (11, 11, 26)
        bg[row * 207:row * 207 + 213] = small_array
    Image.fromarray(bg, mode="RGB").save(PREVIEW)


def main() -> int:
    cruise = read_sheet(CRUISE)
    boost = read_sheet(BOOST)
    cruise_report = report("cruise", cruise)
    boost_report = report("boost", boost)

    cruise_cells = cruise_report["cells"]
    boost_cells = boost_report["cells"]
    cruise_height = max(cell["height"] for cell in cruise_cells)
    boost_height = max(cell["height"] for cell in boost_cells)
    cruise_white = sum(cell["bone_white"] for cell in cruise_cells)
    boost_white = sum(cell["bone_white"] for cell in boost_cells)
    if boost_height <= cruise_height:
        raise AssertionError("boost flame is not taller than cruise")
    if boost_white <= cruise_white:
        raise AssertionError("boost has no more bone-white pixels than cruise")

    print("sheet   size       palette pixels (brown amber light_gold bone_white red green)")
    for item in (cruise_report, boost_report):
        values = item["pixels"]
        print(
            f"{item['name']:7} 1536x1024  "
            f"{values[(126,78,12)]:6} {values[(217,154,26)]:6} {values[(255,211,106)]:6} "
            f"{values[(244,234,212)]:6} {values[(216,35,44)]:6} {values[GREEN]:7}"
        )
    print("\nframe  cruise(h,w,white,red)  boost(h,w,white,red)")
    for index, (left, right) in enumerate(zip(cruise_cells, boost_cells)):
        print(
            f"{index:5}  "
            f"{left['height']:4},{left['width']:3},{left['bone_white']:5},{left['sparks']:3}       "
            f"{right['height']:4},{right['width']:3},{right['bone_white']:5},{right['sparks']:3}"
        )
    print(f"\nboost height max: {boost_height} > cruise height max: {cruise_height}")
    print(f"boost bone-white pixels: {boost_white} > cruise: {cruise_white}")
    make_preview(cruise, boost)
    print(f"preview: {PREVIEW}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
