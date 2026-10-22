#!/usr/bin/env python3
"""Normalize a generated intact saucer to the J1 canvas without smoothing."""
from __future__ import annotations

import sys
from collections import deque
from pathlib import Path

from PIL import Image


GREEN = (0, 255, 0)


def greenish(rgb: tuple[int, int, int]) -> bool:
    r, g, b = rgb
    return g > 105 and g > r * 1.16 and g > b * 1.16


def main() -> None:
    if len(sys.argv) != 3:
        raise SystemExit("usage: prepare_state0.py GENERATED.png OUTPUT.png")
    source, output = map(Path, sys.argv[1:])
    src = Image.open(source).convert("RGB")
    # Keep the old 928 px silhouette width (about 90.6% of 1024) while retaining
    # the generated painting.  This candidate's source silhouette is wider, so
    # its whole-canvas nearest-neighbour scale is 968 px.
    scaled_size = 968
    src = src.resize((scaled_size, scaled_size), Image.Resampling.NEAREST)
    canvas = Image.new("RGB", (1024, 1024), GREEN)
    offset = ((1024 - scaled_size) // 2, (1024 - scaled_size) // 2)
    canvas.paste(src, offset)

    # Key only the connected chroma-green backdrop.  The generated ship contains
    # no green, so this preserves the crisp natural edge while making the backdrop
    # exactly the requested #00FF00.
    px = canvas.load()
    w, h = canvas.size
    seen = bytearray(w * h)
    q: deque[tuple[int, int]] = deque()
    for x in range(w):
        q.extend(((x, 0), (x, h - 1)))
    for y in range(1, h - 1):
        q.extend(((0, y), (w - 1, y)))
    while q:
        x, y = q.popleft()
        idx = y * w + x
        if seen[idx] or not greenish(px[x, y]):
            continue
        seen[idx] = 1
        px[x, y] = GREEN
        if x:
            q.append((x - 1, y))
        if x + 1 < w:
            q.append((x + 1, y))
        if y:
            q.append((x, y - 1))
        if y + 1 < h:
            q.append((x, y + 1))

    output.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(output, format="PNG", optimize=False)


if __name__ == "__main__":
    main()
