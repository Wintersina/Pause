#!/usr/bin/env python3
"""Validate the J2 JadePhantom flame sheets and render a review preview."""

from __future__ import annotations

import argparse
from collections import Counter
from pathlib import Path

from PIL import Image, ImageDraw


KEY = (0, 255, 0)
PALETTE = (
    (14, 90, 68),    # deep green
    (34, 176, 122),  # jade
    (142, 240, 196), # mint
    (244, 234, 212), # warm bone-white
    (31, 181, 185),  # teal
)
ALLOWED = set(PALETTE) | {KEY}
CELL_W = 256
CELL_H = 1024
FRAME_COUNT = 8


def colour_counts(image: Image.Image) -> Counter[tuple[int, int, int]]:
    return Counter(image.getdata())


def make_preview(cruise: Image.Image, boost: Image.Image, destination: Path) -> None:
    scale = 4
    sheet_w = CELL_W * scale
    sheet_h = CELL_H * scale
    gap = 28
    label_h = 28
    preview = Image.new("RGB", (sheet_w + 2 * gap, 2 * (sheet_h + label_h) + 3 * gap), (11, 11, 26))
    draw = ImageDraw.Draw(preview)
    for row, (label, image) in enumerate((("CRUISE", cruise), ("BOOST", boost))):
        x = gap
        y = gap + row * (sheet_h + label_h + gap)
        preview.paste(image.resize((sheet_w, sheet_h), Image.Resampling.NEAREST), (x, y + label_h))
        draw.text((x, y), label, fill=(244, 234, 212))
    destination.parent.mkdir(parents=True, exist_ok=True)
    preview.save(destination, format="PNG", optimize=False)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("output_dir", type=Path)
    args = parser.parse_args()
    cruise_path = args.output_dir / "flame_cruise.png"
    boost_path = args.output_dir / "flame_boost.png"
    preview_path = args.output_dir / "preview_j2.png"

    all_ok = True
    sheets = []
    for label, path in (("cruise", cruise_path), ("boost", boost_path)):
        if not path.is_file():
            print(f"{label:>7} MISSING {path}")
            all_ok = False
            continue
        image = Image.open(path).convert("RGB")
        expected_size = (CELL_W * FRAME_COUNT, CELL_H)
        counts = colour_counts(image)
        bad = set(counts) - ALLOWED
        equal_cells = image.width == CELL_W * FRAME_COUNT and image.height == CELL_H and image.width // FRAME_COUNT == CELL_W
        ok = image.size == expected_size and equal_cells and not bad
        all_ok = all_ok and ok
        bad_text = "none" if not bad else ", ".join(f"#{r:02X}{g:02X}{b:02X}" for r, g, b in sorted(bad))
        print(f"{label:>7} {'PASS' if ok else 'FAIL'} size={image.width}x{image.height} cells=8x{CELL_W} bad={bad_text}")
        print("         " + " ".join(f"#{r:02X}{g:02X}{b:02X}:{counts.get((r, g, b), 0)}" for r, g, b in (*PALETTE, KEY)))
        sheets.append(image)

    if len(sheets) == 2:
        make_preview(sheets[0], sheets[1], preview_path)
        print(f"preview {preview_path} ({Image.open(preview_path).size[0]}x{Image.open(preview_path).size[1]})")
    else:
        all_ok = False
    raise SystemExit(0 if all_ok else 1)


if __name__ == "__main__":
    main()
