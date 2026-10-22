from __future__ import annotations

import colorsys
import random
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "j2_generated_source.png"
MAGENTA = (255, 0, 255)
FOREST = (42, 94, 30)
LEAF = (93, 187, 58)
TEAL = (31, 181, 185)
PALE = (182, 238, 138)
BONE = (244, 234, 212)
PALETTE = (FOREST, LEAF, TEAL, PALE, BONE)
CELL_W = 256
CELL_H = 1024
TOP = 28


def is_generated_key(pixel: tuple[int, int, int]) -> bool:
    r, g, b = pixel
    h, s, _ = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
    return (
        (r > 150 and b > 150 and g < 170 and abs(r - b) < 125 and s > 0.20)
        or pixel == MAGENTA
        or (s > 0.55 and 0.78 < h < 0.99 and g < 150)
    )


def nearest(pixel: tuple[int, int, int]) -> tuple[int, int, int]:
    return min(PALETTE, key=lambda color: sum((a - b) ** 2 for a, b in zip(pixel, color)))


def load_painted_crop() -> Image.Image:
    image = Image.open(SOURCE).convert("RGB")
    pixels = image.load()
    points = [
        (x, y)
        for y in range(image.height)
        for x in range(image.width)
        if not is_generated_key(pixels[x, y])
    ]
    if not points:
        raise RuntimeError("generated source contains no painted pixels")
    box = (
        min(x for x, _ in points),
        min(y for _, y in points),
        max(x for x, _ in points) + 1,
        max(y for _, y in points) + 1,
    )
    crop = image.crop(box)
    out = Image.new("RGB", crop.size, MAGENTA)
    out_pixels = out.load()
    for y in range(crop.height):
        for x in range(crop.width):
            pixel = crop.getpixel((x, y))
            out_pixels[x, y] = MAGENTA if is_generated_key(pixel) else nearest(pixel)
    return out


def recolor_smear(image: Image.Image) -> None:
    pixels = image.load()
    for y in range(image.height):
        for x in range(image.width):
            pixel = pixels[x, y]
            if pixel == MAGENTA:
                continue
            # A narrow, near-solid bone-white speed streak, with just enough
            # pale/teal edge to retain the Paranoid flame identity.
            if pixel == FOREST:
                pixels[x, y] = TEAL if (x + y) % 11 == 0 else PALE
            elif pixel == TEAL:
                pixels[x, y] = TEAL if (x * 3 + y) % 13 == 0 else PALE
            elif pixel == LEAF:
                pixels[x, y] = PALE
            else:
                pixels[x, y] = BONE


def hotten_boost(image: Image.Image) -> None:
    pixels = image.load()
    for y in range(image.height):
        for x in range(image.width):
            pixel = pixels[x, y]
            if pixel == LEAF and (x + y) % 3 == 0:
                pixels[x, y] = PALE
            elif pixel == TEAL and (x * 5 + y) % 9 == 0:
                pixels[x, y] = PALE
            elif pixel == PALE and (x + y) % 4 == 0:
                pixels[x, y] = BONE


def add_sparks(cell: Image.Image, frame: int, length: int, boost: bool) -> None:
    draw = ImageDraw.Draw(cell)
    rng = random.Random((1000 if boost else 0) + frame * 97)
    center = CELL_W // 2
    count = 11 if boost else 6
    for index in range(count):
        side = -1 if index % 2 == 0 else 1
        y = TOP + rng.randint(max(10, length // 7), max(12, length - 10))
        spread = rng.randint(48, 90) if boost else rng.randint(40, 72)
        x = center + side * spread + rng.randint(-7, 7)
        size = rng.choice((1, 1, 2, 2, 3))
        color = BONE if index % 3 == 0 else (PALE if index % 3 == 1 else TEAL)
        draw.rectangle((x, y, x + size, y + size + rng.choice((1, 2, 3))), fill=color)


def make_frame(source: Image.Image, length: int, width_scale: float, frame: int, boost: bool, smear: bool) -> Image.Image:
    ratio = source.width / source.height
    width = max(46, int(length * ratio * width_scale))
    scaled = source.resize((width, length), Image.Resampling.NEAREST)
    if smear:
        recolor_smear(scaled)
    elif boost:
        hotten_boost(scaled)
    cell = Image.new("RGB", (CELL_W, CELL_H), MAGENTA)
    x = (CELL_W - width) // 2
    cell.paste(scaled, (x, TOP))
    add_sparks(cell, frame, length, boost)
    return cell


def make_sheet(name: str, lengths: tuple[int, ...], width_scale: float, boost: bool) -> None:
    source = load_painted_crop()
    sheet = Image.new("RGB", (CELL_W * 6, CELL_H), MAGENTA)
    for frame, length in enumerate(lengths):
        smear = frame == 2
        frame_image = make_frame(source, length, width_scale * (0.72 if smear else 1.0), frame, boost, smear)
        sheet.paste(frame_image, (frame * CELL_W, 0))
    sheet.save(ROOT / name, format="PNG", optimize=False)


def main() -> None:
    make_sheet("flame_cruise.png", (650, 680, 760, 665, 632, 652), 0.90, False)
    make_sheet("flame_boost.png", (850, 880, 990, 865, 820, 850), 1.07, True)


if __name__ == "__main__":
    main()
