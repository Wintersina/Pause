#!/usr/bin/env python3
"""Validate and preview the J2 VoltViper engine-flame sheets."""

from __future__ import annotations

from collections import Counter
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
EXPECTED_SIZE = (1792, 1024)
FRAME_COUNT = 7
CELL_SIZE = (256, 1024)
BACKGROUND = (0, 255, 0)
PREVIEW_BACKGROUND = (11, 11, 26)
ALLOWED = {
    (23, 76, 140),    # #174C8C deep blue
    (46, 155, 230),   # #2E9BE6 azure
    (110, 242, 238),  # #6EF2EE cyan
    (244, 234, 212),  # #F4EAD4 bone-white
    (242, 134, 43),   # #F2862B orange
    BACKGROUND,
}


def non_key_bounds(image: Image.Image, x0: int, x1: int) -> tuple[int, int, int, int] | None:
    pixels = image.load()
    points = [
        (x, y)
        for y in range(image.height)
        for x in range(x0, x1)
        if pixels[x, y] != BACKGROUND
    ]
    if not points:
        return None
    xs, ys = zip(*points)
    return min(xs), min(ys), max(xs) + 1, max(ys) + 1


def validate(name: str) -> tuple[Image.Image, bool]:
    path = ROOT / name
    image = Image.open(path).convert("RGB")
    pixel_data = image.get_flattened_data() if hasattr(image, "get_flattened_data") else image.getdata()
    colors = Counter(pixel_data)
    bad_colors = sorted(set(colors) - ALLOWED)
    cells_ok = image.width % FRAME_COUNT == 0 and image.width // FRAME_COUNT == CELL_SIZE[0]
    size_ok = image.size == EXPECTED_SIZE
    print(
        f"{name:<18} size={image.width}x{image.height:<4} "
        f"cells={image.width // FRAME_COUNT if image.width else 0}x{image.height:<4} "
        f"unique={len(colors):<2} bad={len(bad_colors):<2} "
        f"layout={'PASS' if size_ok and cells_ok else 'FAIL'} palette={'PASS' if not bad_colors else 'FAIL'}"
    )
    if bad_colors:
        print(f"  disallowed: {bad_colors[:8]}")
    if size_ok and cells_ok:
        tails = []
        for frame in range(FRAME_COUNT):
            bbox = non_key_bounds(image, frame * CELL_SIZE[0], (frame + 1) * CELL_SIZE[0])
            tails.append("--" if bbox is None else f"{bbox[3] / image.height:.0%}")
        print(f"  frame tail reach: {', '.join(tails)}")
    return image, size_ok and cells_ok and not bad_colors


def save_preview(images: list[Image.Image]) -> None:
    scale = 2
    width = EXPECTED_SIZE[0] // scale
    height = EXPECTED_SIZE[1] // scale
    gap = 32
    preview = Image.new("RGB", (width + gap * 2, height * 2 + gap * 3), PREVIEW_BACKGROUND)
    for index, image in enumerate(images):
        small = image.resize((width, height), Image.Resampling.NEAREST)
        preview.paste(small, (gap, gap + index * (height + gap)))
    preview.save(ROOT / "preview_j2.png", format="PNG", optimize=False)


def main() -> int:
    print("file               size       cells       unique bad layout palette")
    print("---------------------------------------------------------------------")
    images = []
    valid = True
    for name in ("flame_cruise.png", "flame_boost.png"):
        image, image_valid = validate(name)
        images.append(image)
        valid &= image_valid
    save_preview(images)
    print(f"preview_j2.png saved: {ROOT / 'preview_j2.png'}")
    print(f"\nJ2 CHECK: {'PASS' if valid else 'FAIL'}")
    return 0 if valid else 1


if __name__ == "__main__":
    raise SystemExit(main())
