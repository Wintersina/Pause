#!/usr/bin/env python3
"""Validate the J2 Ninja spin-effect flipbooks and make a review preview."""

from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
FILES = ("ring_cruise.png", "ring_boost.png", "wake_cruise.png", "wake_boost.png")
SIZE = (4096, 512)
CELL = 512
FRAMES = 8
GREEN = np.array([0, 255, 0], dtype=np.uint8)
PALETTE = {
    (0x4A, 0x58, 0xC8),
    (0x9F, 0xA8, 0xF0),
    (0xF4, 0xEA, 0xD4),
    (0x23, 0x2A, 0x6E),
    (0xD8, 0x23, 0x2C),
    (0, 255, 0),
}


def load(name: str) -> np.ndarray:
    path = ROOT / name
    image = Image.open(path)
    if image.mode != "RGB":
        raise AssertionError(f"{name}: expected RGB PNG, got {image.mode}")
    if image.size != SIZE:
        raise AssertionError(f"{name}: expected {SIZE[0]}x{SIZE[1]}, got {image.size}")
    return np.asarray(image, dtype=np.uint8)


def cells(sheet: np.ndarray) -> list[np.ndarray]:
    return [sheet[:, index * CELL : (index + 1) * CELL] for index in range(FRAMES)]


def foreground(cell: np.ndarray) -> np.ndarray:
    return np.any(cell != GREEN, axis=2)


def non_green(cell: np.ndarray) -> int:
    return int(np.count_nonzero(foreground(cell)))


def brightness(cell: np.ndarray) -> float:
    mask = foreground(cell)
    if not np.any(mask):
        return 0.0
    # Mean luma over painted pixels, excluding the key colour.
    rgb = cell[mask].astype(np.float64)
    return float(np.mean(rgb @ np.array([0.2126, 0.7152, 0.0722])))


def palette_errors(sheet: np.ndarray) -> int:
    flat = sheet.reshape(-1, 3)
    return sum(tuple(pixel) not in PALETTE for pixel in flat)


def ring_centre_errors(cell: np.ndarray) -> int:
    yy, xx = np.mgrid[0:CELL, 0:CELL]
    centre = (xx - CELL / 2.0) ** 2 + (yy - CELL / 2.0) ** 2 <= 120.0**2
    return int(np.count_nonzero(foreground(cell) & centre))


def save_preview(sheets: dict[str, np.ndarray]) -> None:
    preview = Image.new("RGB", (SIZE[0], SIZE[1] * 4), (0x0B, 0x0B, 0x1A))
    for row, name in enumerate(FILES):
        image = Image.fromarray(sheets[name], mode="RGB")
        key = Image.new("RGB", image.size, (0x0B, 0x0B, 0x1A))
        mask = Image.fromarray(np.any(sheets[name] != GREEN, axis=2).astype(np.uint8) * 255, mode="L")
        key.paste(image, mask=mask)
        preview.paste(key, (0, row * SIZE[1]))
    preview.save(ROOT / "preview_j2.png", format="PNG", optimize=False)


def main() -> int:
    failed = False
    sheets: dict[str, np.ndarray] = {}
    for name in FILES:
        try:
            sheets[name] = load(name)
        except (AssertionError, FileNotFoundError) as error:
            print(f"FAIL {error}")
            failed = True

    if failed:
        return 1

    print("file              cells  palette errors  painted px (avg)   luma (avg)   centre errors")
    for name in FILES:
        sheet_data = sheets[name]
        sheet_cells = cells(sheet_data)
        errors = palette_errors(sheet_data)
        counts = [non_green(cell) for cell in sheet_cells]
        lumas = [brightness(cell) for cell in sheet_cells]
        centres = [ring_centre_errors(cell) for cell in sheet_cells] if name.startswith("ring_") else [0] * FRAMES
        equal = all(cell.shape == (CELL, CELL, 3) for cell in sheet_cells)
        print(
            f"{name:<17} {int(equal):>5} {errors:>15} "
            f"{sum(counts):>7} ({sum(counts)/FRAMES:7.1f}) "
            f"{sum(lumas)/FRAMES:>11.2f} {sum(centres):>14}"
        )
        failed |= not equal or errors != 0 or any(centres)

    cruise_ring = sheets["ring_cruise.png"]
    boost_ring = sheets["ring_boost.png"]
    cruise_wake = sheets["wake_cruise.png"]
    boost_wake = sheets["wake_boost.png"]
    ring_cruise_px = sum(non_green(cell) for cell in cells(cruise_ring))
    ring_boost_px = sum(non_green(cell) for cell in cells(boost_ring))
    wake_cruise_px = sum(non_green(cell) for cell in cells(cruise_wake))
    wake_boost_px = sum(non_green(cell) for cell in cells(boost_wake))
    ring_cruise_luma = sum(brightness(cell) for cell in cells(cruise_ring)) / FRAMES
    ring_boost_luma = sum(brightness(cell) for cell in cells(boost_ring)) / FRAMES
    wake_cruise_luma = sum(brightness(cell) for cell in cells(cruise_wake)) / FRAMES
    wake_boost_luma = sum(brightness(cell) for cell in cells(boost_wake)) / FRAMES

    print("\nboost comparisons")
    print(f"ring: painted {ring_boost_px} > {ring_cruise_px}; luma {ring_boost_luma:.2f} > {ring_cruise_luma:.2f}")
    print(f"wake: painted {wake_boost_px} > {wake_cruise_px}; luma {wake_boost_luma:.2f} > {wake_cruise_luma:.2f}")
    failed |= ring_boost_px <= ring_cruise_px or ring_boost_luma <= ring_cruise_luma
    failed |= wake_boost_px <= wake_cruise_px or wake_boost_luma <= wake_cruise_luma

    save_preview(sheets)
    print("\nframe holds: 2,2,1,2,2,2,2,2 (24 fps ticks)")
    print("preview: preview_j2.png")
    print("\nRESULT: " + ("FAIL" if failed else "PASS"))
    return int(failed)


if __name__ == "__main__":
    sys.exit(main())
