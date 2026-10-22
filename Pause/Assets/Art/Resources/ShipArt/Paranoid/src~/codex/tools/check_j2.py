from __future__ import annotations

import warnings
from pathlib import Path

from PIL import Image


warnings.filterwarnings("ignore", category=DeprecationWarning)


ROOT = Path(__file__).resolve().parents[1]
MAGENTA = (255, 0, 255)
PREVIEW_BG = (11, 11, 26)
PALETTE = {
    (42, 94, 30),
    (93, 187, 58),
    (31, 181, 185),
    (182, 238, 138),
    (244, 234, 212),
}
ALLOWED = PALETTE | {MAGENTA}
SHEET_SIZE = (1536, 1024)
CELL_W = 256
CELL_H = 1024


def load(name: str) -> Image.Image:
    image = Image.open(ROOT / name).convert("RGB")
    if image.size != SHEET_SIZE:
        raise AssertionError(f"{name}: expected 1536x1024, got {image.size}")
    return image


def cell_pixels(image: Image.Image, frame: int):
    return [
        image.getpixel((frame * CELL_W + x, y))
        for y in range(CELL_H)
        for x in range(CELL_W)
    ]


def bbox(image: Image.Image, frame: int):
    points = []
    for y in range(CELL_H):
        for x in range(CELL_W):
            if image.getpixel((frame * CELL_W + x, y)) != MAGENTA:
                points.append((x, y))
    if not points:
        return None
    return (
        min(x for x, _ in points),
        min(y for _, y in points),
        max(x for x, _ in points),
        max(y for _, y in points),
    )


def brightness(pixels) -> int:
    return sum(2126 * r + 7152 * g + 722 * b for r, g, b in pixels if (r, g, b) != MAGENTA)


def lengths(image: Image.Image) -> list[int]:
    values = []
    for frame in range(6):
        bounds = bbox(image, frame)
        if bounds is None:
            raise AssertionError(f"frame {frame}: empty flame")
        values.append(bounds[3] - bounds[1] + 1)
    return values


def save_preview(cruise: Image.Image, boost: Image.Image) -> None:
    canvas = Image.new("RGB", (SHEET_SIZE[0], SHEET_SIZE[1] * 2 + 24), PREVIEW_BG)
    for image, y_offset in ((cruise, 0), (boost, SHEET_SIZE[1] + 24)):
        keyed = Image.new("RGB", image.size, PREVIEW_BG)
        source = image.load()
        target = keyed.load()
        for y in range(image.height):
            for x in range(image.width):
                pixel = source[x, y]
                target[x, y] = PREVIEW_BG if pixel == MAGENTA else pixel
        canvas.paste(keyed, (0, y_offset))
    canvas.save(ROOT / "preview_j2.png", format="PNG", optimize=False)


def main() -> None:
    cruise = load("flame_cruise.png")
    boost = load("flame_boost.png")
    for name, image in (("flame_cruise.png", cruise), ("flame_boost.png", boost)):
        bad = sorted(set(image.getdata()) - ALLOWED)
        if bad:
            raise AssertionError(f"{name}: non-palette pixels found: {bad[:8]}")
        if image.width % 6 != 0 or image.width // 6 != CELL_W:
            raise AssertionError(f"{name}: cells are not six equal 256px columns")

    cruise_lengths = lengths(cruise)
    boost_lengths = lengths(boost)
    cruise_brightness = brightness(cruise.getdata())
    boost_brightness = brightness(boost.getdata())
    ratio = boost_brightness / cruise_brightness
    average_cruise = sum(cruise_lengths) / 6
    average_boost = sum(boost_lengths) / 6

    print("file          frame  bbox                  flame_length_px")
    for name, image in (("cruise", cruise), ("boost", boost)):
        for frame in range(6):
            print(f"{name:12} {frame:5}  {str(bbox(image, frame)):20} {lengths(image)[frame]:16}")
    print(f"\nbrightness boost/cruise: {ratio:.3f}x")
    print(f"average flame length cruise/boost: {average_cruise:.1f}px / {average_boost:.1f}px")

    assert average_boost > average_cruise, "boost must be longer than cruise"
    assert ratio > 1.15, "boost must be more than 1.15x as bright as cruise"
    assert boost_lengths[2] >= 960, "boost smear must reach about 97% of its cell"
    assert cruise_lengths[2] > sum(cruise_lengths[i] for i in (0, 1, 3, 4, 5)) / 5 * 1.10
    assert boost_lengths[2] > sum(boost_lengths[i] for i in (0, 1, 3, 4, 5)) / 5 * 1.10
    save_preview(cruise, boost)
    print("\nPASS: exact palette, six equal cells, longer/brighter boost, preview_j2.png written")


if __name__ == "__main__":
    main()
