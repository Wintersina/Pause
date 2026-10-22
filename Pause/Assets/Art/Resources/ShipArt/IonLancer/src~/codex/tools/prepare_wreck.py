#!/usr/bin/env python3
"""Normalize a generated wreck and force the approved IonLancer silhouette."""

from __future__ import annotations

import argparse
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image


GREEN = (0, 255, 0)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    source = Image.open(args.source).convert("RGB")
    if source.size != (1024, 1024):
        raise SystemExit(f"source must be 1024x1024, got {source.size}")

    candidate = Image.open(args.candidate).convert("RGB")
    candidate = candidate.resize(source.size, Image.Resampling.NEAREST)

    src = source.load()
    gen = candidate.load()
    out = Image.new("RGB", source.size, GREEN)
    dst = out.load()

    # The source's flat chroma green is the only background color. Every other
    # pixel belongs to the approved collision/render silhouette and is retained.
    for y in range(source.height):
        for x in range(source.width):
            if src[x, y] != GREEN:
                color = gen[x, y]
                # Never let generated chroma spill become a hole in the hull.
                green_spill = color[1] > color[0] + 20 and color[1] > color[2] + 20
                if color == GREEN or green_spill:
                    color = src[x, y]
                dst[x, y] = color

    # Image generation leaves a few broad near-intact patches. Extend the
    # nearest generated damage treatment into those patches with hard pixel
    # copies only; this is a cleanup pass, never a blur or alpha overlay.
    source_array = np.asarray(source, dtype=np.int16)
    out_array = np.asarray(out, dtype=np.int16)

    source_mask = np.any(source_array != np.asarray(GREEN, dtype=np.int16), axis=2)
    damage = np.abs(source_array - out_array).sum(axis=2) > 60
    damage &= source_mask

    # Multi-source breadth-first propagation copies the nearest strong damage
    # color into connected near-intact pixels. It preserves stepped pixel runs.
    queue = deque(map(tuple, np.argwhere(damage)))
    visited = damage.copy()
    neighbors = ((-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1))
    while queue:
        y, x = queue.popleft()
        for dy, dx in neighbors:
            yy, xx = y + dy, x + dx
            if 0 <= yy < 1024 and 0 <= xx < 1024 and source_mask[yy, xx] and not visited[yy, xx]:
                visited[yy, xx] = True
                out_array[yy, xx] = out_array[y, x]
                queue.append((yy, xx))

    # A few copied colors can be too close to an individual source pixel. Use
    # the strongest adjacent damaged color for those pixels, again as a hard
    # nearest-neighbor replacement rather than a filter.
    for _ in range(4):
        current_diff = np.abs(source_array - out_array).sum(axis=2)
        strong = (current_diff > 60) & source_mask
        for y, x in np.argwhere(source_mask & ~strong):
            best_color = None
            best_difference = 60
            for dy, dx in neighbors:
                yy, xx = y + dy, x + dx
                if 0 <= yy < 1024 and 0 <= xx < 1024 and strong[yy, xx]:
                    difference = int(np.abs(source_array[y, x] - out_array[yy, xx]).sum())
                    if difference > best_difference:
                        best_difference = difference
                        best_color = out_array[yy, xx].copy()
            if best_color is not None:
                out_array[y, x] = best_color

    # Final chroma guard: the approved wreck palette has no green material.
    greenish = source_mask & (out_array[:, :, 1] > out_array[:, :, 0] + 20) & (
        out_array[:, :, 1] > out_array[:, :, 2] + 20
    )
    source_greenish = (source_array[:, :, 1] > source_array[:, :, 0] + 20) & (
        source_array[:, :, 1] > source_array[:, :, 2] + 20
    )
    out_array[greenish & ~source_greenish] = source_array[greenish & ~source_greenish]
    out_array[greenish & source_greenish] = (11, 15, 30)

    out = Image.fromarray(out_array.astype("uint8"), "RGB")

    args.output.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.output, format="PNG", optimize=False)


if __name__ == "__main__":
    main()
