#!/usr/bin/env python3
"""Validate J2 flame sheets and generate the review preview."""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image


KEY = (0, 255, 0)
PALETTE = {
    (110, 48, 20): "dark brown",
    (200, 102, 46): "copper",
    (255, 180, 60): "amber",
    (244, 234, 212): "bone-white",
    (110, 242, 238): "cyan",
    KEY: "key green",
}
CELL_W = 256
CELL_H = 1024
CELLS = 6


def analyse(path: Path) -> tuple[list[dict], set[tuple[int, int, int]]]:
    image = Image.open(path).convert("RGB")
    if image.size != (CELL_W * CELLS, CELL_H):
        raise AssertionError(f"{path.name}: expected 1536x1024, got {image.size}")
    arr = np.asarray(image)
    colors = {tuple(int(v) for v in color) for color in np.unique(arr.reshape(-1, 3), axis=0)}
    unknown = colors - set(PALETTE)
    if unknown:
        raise AssertionError(f"{path.name}: disallowed colors: {sorted(unknown)[:8]}")

    rows = []
    for index in range(CELLS):
        x0 = index * CELL_W
        cell = arr[:, x0:x0 + CELL_W]
        border = np.concatenate((cell[0, :, :], cell[-1, :, :], cell[:, 0, :], cell[:, -1, :]), axis=0)
        if np.any(np.any(border != np.asarray(KEY, dtype=np.uint8), axis=1)):
            raise AssertionError(f"{path.name}: non-key pixel on cell {index} border")
        mask = np.any(cell != np.asarray(KEY, dtype=np.uint8), axis=2)
        ys, xs = np.where(mask)
        if len(xs) == 0:
            raise AssertionError(f"{path.name}: empty cell {index}")
        counts = {name: int(np.all(cell == color, axis=2).sum()) for color, name in PALETTE.items() if color != KEY}
        non_key = int(mask.sum())
        # Judge heat in the central 60% where the white-hot core and cyan
        # torch cone live; detached sparks do not dilute the result.
        core = cell[:, CELL_W // 5:4 * CELL_W // 5]
        core_mask = np.any(core != np.asarray(KEY, dtype=np.uint8), axis=2)
        core_hot = (
            np.all(core == (255, 180, 60), axis=2)
            | np.all(core == (244, 234, 212), axis=2)
            | np.all(core == (110, 242, 238), axis=2)
        )
        hot = int(core_hot.sum())
        core_pixels = max(1, int(core_mask.sum()))
        rows.append({
            "frame": index,
            "bbox": (int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())),
            "height": int(ys.max() - ys.min() + 1),
            "width": int(xs.max() - xs.min() + 1),
            "pixels": non_key,
            "core_pixels": core_pixels,
            "hot_share": hot / core_pixels,
            "hot_pixels": hot,
        })
    return rows, colors


def print_table(name: str, rows: list[dict], colors: set[tuple[int, int, int]]) -> None:
    print(f"{name}: colors={len(colors)}")
    print("frame  bbox                 size       pixels  hot-share")
    for row in rows:
        x0, y0, x1, y1 = row["bbox"]
        print(f"{row['frame']:>5}  ({x0:>3},{y0:>4})-({x1:>3},{y1:>4})  "
              f"{row['width']:>3}x{row['height']:<4}  {row['pixels']:>6}  {row['hot_share']:.3f}")


def save_preview(cruise: Path, boost: Path, output: Path) -> None:
    dark = (11, 11, 26)
    preview = Image.new("RGB", (CELL_W * CELLS, CELL_H * 2), dark)
    for y, path in ((0, cruise), (CELL_H, boost)):
        image = Image.open(path).convert("RGB")
        arr = np.asarray(image).copy()
        arr[np.all(arr == np.asarray(KEY, dtype=np.uint8), axis=2)] = dark
        preview.paste(Image.fromarray(arr, mode="RGB"), (0, y))
    preview.save(output)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dir", type=Path, required=True)
    args = parser.parse_args()
    cruise = args.dir / "flame_cruise.png"
    boost = args.dir / "flame_boost.png"
    preview = args.dir / "preview_j2.png"

    cruise_rows, cruise_colors = analyse(cruise)
    boost_rows, boost_colors = analyse(boost)
    print_table("flame_cruise.png", cruise_rows, cruise_colors)
    print_table("flame_boost.png", boost_rows, boost_colors)

    cruise_tail = max(row["bbox"][3] for row in cruise_rows)
    boost_tail = max(row["bbox"][3] for row in boost_rows)
    cruise_hot = sum(row["hot_pixels"] for row in cruise_rows) / sum(row["core_pixels"] for row in cruise_rows)
    boost_hot = sum(row["hot_pixels"] for row in boost_rows) / sum(row["core_pixels"] for row in boost_rows)
    if boost_tail <= cruise_tail:
        raise AssertionError(f"boost is not longer: {boost_tail} <= {cruise_tail}")
    if boost_hot <= cruise_hot:
        raise AssertionError(f"boost is not brighter/hotter: {boost_hot:.3f} <= {cruise_hot:.3f}")
    print(f"comparison: boost tail={boost_tail} > cruise tail={cruise_tail}; "
          f"hot-share={boost_hot:.3f} > {cruise_hot:.3f}")
    save_preview(cruise, boost, preview)
    print(f"preview: {preview}")


if __name__ == "__main__":
    main()
