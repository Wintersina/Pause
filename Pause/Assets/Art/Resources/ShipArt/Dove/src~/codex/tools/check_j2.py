"""Validate and preview the J2 Dove flame flipbooks."""

from __future__ import annotations

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
CELL_W = 256
CELL_H = 1024
FRAME_COUNT = 7
GREEN = (0, 255, 0)
PALETTE = {
    GREEN,
    (0x14, 0x6A, 0x70),
    (0x36, 0xC2, 0xC2),
    (0xA6, 0xF2, 0xEE),
    (0xF4, 0xEA, 0xD4),
    (0xFF, 0xB4, 0x3C),
}


def load(name: str) -> Image.Image:
    image = Image.open(ROOT / name).convert("RGB")
    expected = (CELL_W * FRAME_COUNT, CELL_H)
    if image.size != expected:
        raise ValueError(f"{name}: expected {expected[0]}x{expected[1]}, got {image.size}")
    return image


def frame(image: Image.Image, index: int) -> Image.Image:
    return image.crop((index * CELL_W, 0, (index + 1) * CELL_W, CELL_H))


def points(image: Image.Image):
    return [(x, y) for y in range(image.height) for x in range(image.width) if image.getpixel((x, y)) != GREEN]


def bbox(image: Image.Image):
    solid = points(image)
    if not solid:
        return None
    return min(x for x, _ in solid), min(y for _, y in solid), max(x for x, _ in solid), max(y for _, y in solid)


def brightness(image: Image.Image) -> int:
    total = 0
    for pixel in image.getdata():
        if pixel != GREEN:
            total += 299 * pixel[0] + 587 * pixel[1] + 114 * pixel[2]
    return total


def sheet_stats(image: Image.Image):
    frame_boxes = [bbox(frame(image, index)) for index in range(FRAME_COUNT)]
    ys = [value for box in frame_boxes if box for value in (box[1], box[3])]
    return frame_boxes, min(ys), max(ys), max(ys) - min(ys) + 1


def save_preview(images: dict[str, Image.Image]) -> None:
    # Keep every keyed pixel dark in the preview while preserving crisp flame pixels.
    preview = Image.new("RGB", (896, 1024), (0x0B, 0x0B, 0x1A))
    for row, name in enumerate(("flame_cruise.png", "flame_boost.png")):
        source = images[name]
        small = Image.new("RGB", (896, 512), (0x0B, 0x0B, 0x1A))
        src = source.load()
        dst = small.load()
        for y in range(512):
            source_y = y * 2
            for x in range(896):
                source_x = x * 2
                pixel = src[source_x, source_y]
                if pixel != GREEN:
                    dst[x, y] = pixel
        preview.paste(small, (0, row * 512))
    preview.save(ROOT / "preview_j2.png", format="PNG", optimize=False)


def main() -> None:
    images = {name: load(name) for name in ("flame_cruise.png", "flame_boost.png")}
    allowed_ok = True
    print("file             size          colors  brightness       flame length  frame tails")
    stats = {}
    for name, image in images.items():
        colors = set(image.getdata())
        unknown = colors - PALETTE
        if unknown:
            allowed_ok = False
            print(f"{name}: unexpected colors: {sorted(unknown)[:8]}")
        frame_boxes, top, bottom, length = sheet_stats(image)
        tails = [box[3] if box else None for box in frame_boxes]
        stats[name] = brightness(image)
        print(f"{name:16} {image.width}x{image.height}   {len(colors):>6}  {stats[name]:>14}   {length:>6}px       {tails}")

    cruise = images["flame_cruise.png"]
    boost = images["flame_boost.png"]
    cruise_brightness = stats["flame_cruise.png"]
    boost_brightness = stats["flame_boost.png"]
    brightness_ratio = boost_brightness / max(1, cruise_brightness)
    print(f"\nboost/cruise brightness ratio: {brightness_ratio:.3f}x")
    print(f"preview: {ROOT / 'preview_j2.png'}")

    ok = (
        allowed_ok
        and boost_brightness > cruise_brightness * 1.1
        and all(bbox(frame(image, index)) is not None for image in images.values() for index in range(FRAME_COUNT))
        and all(bbox(frame(image, index))[1] <= 48 for image in images.values() for index in range(FRAME_COUNT))
        and max(bbox(frame(cruise, index))[3] for index in range(FRAME_COUNT)) >= 780
        and max(bbox(frame(boost, index))[3] for index in range(FRAME_COUNT)) >= 980
    )
    save_preview(images)
    print(f"PASS: {ok}")
    if not ok:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
