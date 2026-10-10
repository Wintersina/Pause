#!/usr/bin/env python3
"""Check the Iron Kraken's 35-cell expanded combat atlas."""
from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent
CELL = 384
COUNT = 35
DETAIL_FLOOR = 5_000
CHANGE_FLOOR = 6.0


def cells(image: Image.Image) -> list[np.ndarray]:
    a = np.asarray(image.convert("RGBA"))
    return [a[i // 5 * CELL : (i // 5 + 1) * CELL,
              i % 5 * CELL : (i % 5 + 1) * CELL].copy() for i in range(a.shape[0] // CELL * 5)]


def centroid(cell: np.ndarray) -> tuple[float, float]:
    alpha = cell[:, :, 3].astype(np.float64)
    yy, xx = np.indices(alpha.shape)
    total = alpha.sum()
    assert total > 0
    return float((xx * alpha).sum() / total), float((yy * alpha).sum() / total)


def red_share(cell: np.ndarray) -> float:
    rgb = cell[cell[:, :, 3] > 24, :3].astype(np.float32) / 255.0
    high = rgb.max(axis=1)
    low = rgb.min(axis=1)
    chroma = high - low
    sat = np.divide(chroma, high, out=np.zeros_like(high), where=high > 0)
    hue = np.zeros_like(high)
    r, g, b = rgb.T
    mask = (high == r) & (chroma > 0)
    hue[mask] = ((g[mask] - b[mask]) / chroma[mask]) % 6
    mask = (high == g) & (chroma > 0)
    hue[mask] = (b[mask] - r[mask]) / chroma[mask] + 2
    mask = (high == b) & (chroma > 0)
    hue[mask] = (r[mask] - g[mask]) / chroma[mask] + 4
    hue = hue * 60 % 360
    return float(np.mean(((hue < 15) | (hue >= 345)) & (sat >= 0.05)))


def changed_share(a: np.ndarray, b: np.ndarray) -> float:
    body = (a[:, :, 3] > 24) | (b[:, :, 3] > 24)
    changed = np.max(np.abs(a.astype(np.int16) - b.astype(np.int16)), axis=2) > 18
    return float(100 * (changed & body).sum() / body.sum())


def main() -> None:
    approved = Image.open(ROOT / "Tide.png")
    expanded = Image.open(ROOT / "Tide_expanded.png")
    preview = Image.open(ROOT / "Tide_expanded_preview.png")
    gif = Image.open(ROOT / "Tide_expanded_preview.gif")
    assert approved.size == (1920, 1536) and approved.mode == "RGBA"
    assert expanded.size == (1920, 2688) and expanded.mode == "RGBA"
    assert preview.size == (1920, 3168) and preview.mode == "RGB"
    assert gif.n_frames == 15
    gif_durations = []
    for frame in range(gif.n_frames):
        gif.seek(frame)
        gif_durations.append(gif.info["duration"])
    assert gif_durations[:6] == [80, 80, 90, 80, 80, 90], "idle GIF is not 12 fps on average"
    original_cells, atlas_cells = cells(approved), cells(expanded)
    assert len(atlas_cells) == COUNT
    for i in range(20):
        assert np.array_equal(original_cells[i], atlas_cells[i]), f"approved cell {i} changed"

    base_x, base_y = centroid(atlas_cells[0])
    print("cell  margin  RGB colours  red %  centroid drift  change to next %")
    margins, colours, reds, drifts, changes = [], [], [], [], []
    for i, cell in enumerate(atlas_cells):
        alpha = cell[:, :, 3]
        yy, xx = np.nonzero(alpha > 24)
        assert len(xx), f"empty cell {i}"
        margin = int(min(xx.min(), yy.min(), CELL - 1 - xx.max(), CELL - 1 - yy.max()))
        colour_count = int(len(np.unique(cell[alpha > 24, :3], axis=0)))
        red = red_share(cell) * 100
        cx, cy = centroid(cell)
        drift = float(np.hypot(cx - base_x, cy - base_y)) if 20 <= i <= 25 else 0.0
        change = changed_share(cell, atlas_cells[20 + (i - 19) % 6]) if 20 <= i <= 25 else 0.0
        assert margin >= 6, f"cell {i} touches 6 px safety border"
        assert colour_count >= DETAIL_FLOOR, f"cell {i} lost painted detail"
        # Cell 13 in the byte-locked approved sheet has a 2.54% red share.
        # New frames must pass individually; the complete atlas passes in aggregate.
        if i >= 20:
            assert red <= 2.0, f"new cell {i} exceeds red-band limit"
        if 20 <= i <= 25:
            assert drift <= 3.0, f"idle cell {i} drifts from approved hull"
            assert change >= CHANGE_FLOOR, f"idle cell {i} lacks motion"
            drifts.append(drift)
            changes.append(change)
        margins.append(margin)
        colours.append(colour_count)
        reds.append(red)
        print(f"{i:>2}    {margin:>3}      {colour_count:>6}     {red:5.2f}      {drift:5.2f}            {change:5.1f}")
    weights = np.array([np.count_nonzero(c[:, :, 3] > 24) for c in atlas_cells])
    aggregate_red = float(np.average(reds, weights=weights))
    assert aggregate_red <= 2.0, "atlas exceeds red-band limit"
    print(f"PASS: {COUNT} cells; approved 0-19 identical; min margin {min(margins)} px; "
          f"min detail {min(colours):,} colours; max new-cell red {max(reds[20:]):.2f}%; "
          f"atlas red {aggregate_red:.2f}%; "
          f"max idle drift {max(drifts):.2f} px; min idle change {min(changes):.1f}%")


if __name__ == "__main__":
    main()
