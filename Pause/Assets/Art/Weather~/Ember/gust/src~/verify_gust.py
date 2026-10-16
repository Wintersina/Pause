from __future__ import annotations

import json
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
IMAGE = ROOT / "wx_gust.png"
CELL = 512
ALLOWED = {
    tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))
    for h in (
        "05060c", "0e0808", "1d1212", "2e1d1a", "47302b", "6a4a40", "957060",
        "6a2404", "c24a06", "f77a0a", "fcb809", "fcee09", "fdfa92",
        "fc7a08", "fdf06a", "fffbe0",
    )
}


def block_replication_fraction(image: Image.Image, factor: int) -> float:
    """Fraction of aligned factor×factor blocks that are perfectly flat."""
    rgba = image.load()
    total = flat = 0
    for y in range(0, image.height - factor + 1, factor):
        for x in range(0, image.width - factor + 1, factor):
            block = [rgba[x + dx, y + dy] for dy in range(factor) for dx in range(factor)]
            if not any(pixel[3] for pixel in block):
                continue
            first = block[0]
            total += 1
            if all(pixel == first for pixel in block):
                flat += 1
    return flat / total


def main() -> None:
    image = Image.open(IMAGE).convert("RGBA")
    assert image.size == (2048, 1024), image.size
    cells = []
    for row in range(2):
        for col in range(4):
            cell = image.crop((col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL))
            alpha = cell.getchannel("A")
            values = list(alpha.getdata())
            bbox = alpha.getbbox()
            assert bbox is not None, (row, col, "empty cell")
            cells.append({
                "row": row,
                "col": col,
                "nonzero": sum(v > 0 for v in values),
                "bbox": bbox,
                "mean_nonzero_alpha": round(sum(v for v in values if v) / sum(v > 0 for v in values), 2),
            })

    # Row 0 is the repeating ribbon family. Compare the outer alpha columns
    # inside each cell; they are deliberately equalized by the compositor.
    seams = []
    for col in range(4):
        cell = image.crop((col * CELL, 0, (col + 1) * CELL, CELL))
        left = list(cell.getchannel("A").crop((0, 0, 1, CELL)).getdata())
        right = list(cell.getchannel("A").crop((CELL - 1, 0, CELL, CELL)).getdata())
        seams.append(max(abs(a - b) for a, b in zip(left, right)))
    assert max(seams) <= 2, seams

    bad_colors = sorted({rgb for r, g, b, a in image.getdata() if a and (rgb := (r, g, b)) not in ALLOWED})
    assert not bad_colors, bad_colors[:8]

    flatness = {factor: round(block_replication_fraction(image, factor), 4) for factor in (2, 3, 4)}
    # A true 2x/3x/4x nearest-neighbour upscale would be nearly all flat blocks;
    # the generated painting is resized at non-integer ratios and stays detailed.
    assert max(flatness.values()) < 0.75, flatness

    print(json.dumps({"size": image.size, "cells": cells, "seam_max_abs_alpha": max(seams), "flat_block_fraction": flatness}, indent=2))


if __name__ == "__main__":
    main()
