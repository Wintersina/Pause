"""Validate the J2 Turtle engine-flame sheets and write preview_j2.png."""

from __future__ import annotations

from pathlib import Path

from PIL import Image


OUT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
PALETTE = {
    (31, 181, 185),   # teal
    (110, 242, 238),  # light cyan
    (244, 234, 212),  # warm bone-white
    (90, 58, 24),     # dark umber
    (226, 190, 132),  # sand
    GREEN,
}
NAMES = ("flame_cruise.png", "flame_boost.png")
CELL_W = 256
CELL_H = 1024
CELLS = 7


def load(name: str) -> Image.Image:
    image = Image.open(OUT / name).convert("RGB")
    if image.size != (CELL_W * CELLS, CELL_H):
        raise SystemExit(f"{name}: expected {CELL_W * CELLS}x{CELL_H}, got {image.size}")
    return image


def frame(image: Image.Image, index: int) -> Image.Image:
    return image.crop((index * CELL_W, 0, (index + 1) * CELL_W, CELL_H))


def non_green_pixels(image: Image.Image) -> list[tuple[int, int, tuple[int, int, int]]]:
    return [(x, y, image.getpixel((x, y)))
            for y in range(image.height)
            for x in range(image.width)
            if image.getpixel((x, y)) != GREEN]


def tail_length(image: Image.Image) -> int:
    points = non_green_pixels(image)
    return 0 if not points else max(y for _, y, _ in points) + 1


def frame_sum(image: Image.Image) -> int:
    return sum(sum(pixel) for _, _, pixel in non_green_pixels(image))


def validate(image: Image.Image, name: str) -> tuple[int, int, list[int]]:
    colours = set(image.get_flattened_data())
    if not colours <= PALETTE:
        bad = sorted(colours - PALETTE)
        raise SystemExit(f"{name}: disallowed colours: {bad[:8]}")
    tails = []
    for index in range(CELLS):
        cell = frame(image, index)
        points = non_green_pixels(cell)
        if not points:
            raise SystemExit(f"{name}: frame {index} is empty")
        if any(cell.getpixel(corner) != GREEN for corner in ((0, 0), (255, 0), (0, 1023), (255, 1023))):
            raise SystemExit(f"{name}: frame {index} touches a corner; possible backdrop rectangle")
        xs = [x for x, _, _ in points]
        ys = [y for _, y, _ in points]
        if min(xs) == 0 or max(xs) == 255:
            raise SystemExit(f"{name}: frame {index} touches a cell edge; possible backdrop rectangle")
        if len(points) >= CELL_W * CELL_H * 0.60:
            raise SystemExit(f"{name}: frame {index} is too full; possible backdrop rectangle")
        tails.append(max(ys) + 1)
    total = frame_sum(image)
    return total, total // CELLS, tails


def write_preview(images: list[Image.Image]) -> None:
    preview = Image.new("RGB", (CELL_W * CELLS, CELL_H * len(images) + 48), (11, 11, 26))
    for row, image in enumerate(images):
        dest = Image.new("RGB", image.size, (11, 11, 26))
        src = image.load()
        out = dest.load()
        for y in range(image.height):
            for x in range(image.width):
                if src[x, y] != GREEN:
                    out[x, y] = src[x, y]
        preview.paste(dest, (0, row * (CELL_H + 16) + 16))
    preview.save(OUT / "preview_j2.png")


images = [load(name) for name in NAMES]
results = [validate(image, name) for image, name in zip(images, NAMES)]
write_preview(images)

print("file              size        sheet sum   per-cell sum   tail px by frame")
for name, image, (total, per_cell, tails) in zip(NAMES, images, results):
    print(f"{name:18} {image.width}x{image.height}  {total:10d}   {per_cell:12d}   {tails}")

cruise_sum = results[0][0]
boost_sum = results[1][0]
ratio = boost_sum / cruise_sum
print(f"boost/cruise non-green RGB-sum ratio: {ratio:.3f} (required >= 1.150)")
if ratio < 1.15:
    raise SystemExit("FAIL: boost is not at least 1.15x cruise")
if not (760 <= min(results[0][2]) <= 900):
    raise SystemExit("FAIL: cruise tail length is outside the expected ~80% band")
if min(results[1][2]) < 930:
    raise SystemExit("FAIL: boost tail does not reach the expected ~97% band")
print("PASS: dimensions, seven equal cells, palette, key background, non-rectangular flame shapes, energy ratio, and tail bands")
