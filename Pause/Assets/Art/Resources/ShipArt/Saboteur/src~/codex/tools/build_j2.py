#!/usr/bin/env python3
"""Build the J2 Saboteur exhaust sheets from the generated painted source."""

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "tools" / "generated_j2_raw_v2.png"
CELL_W, CELL_H, FRAMES = 256, 1024, 8
KEY = (0, 255, 0)
PREVIEW_BG = (11, 11, 26)
PALETTE = [
    (184, 60, 192),   # violet
    (242, 134, 43),   # orange
    (244, 234, 212),  # bone-white
    (94, 26, 102),    # deep violet
    (58, 42, 92),     # indigo
    KEY,
]


def nearest(rgb):
    return min(PALETTE, key=lambda c: sum((int(rgb[i]) - c[i]) ** 2 for i in range(3)))


def quantize(image):
    out = Image.new("RGB", image.size)
    out.putdata([nearest(px) for px in image.get_flattened_data()])
    return out


def make_frame(source, target_height):
    # Equal source cells first, then nearest-neighbour scaling to the game cell.
    frame = source.resize((CELL_W, CELL_H), Image.Resampling.NEAREST)
    pixels = frame.load()
    non_key = [
        (x, y)
        for y in range(CELL_H)
        for x in range(CELL_W)
        if pixels[x, y] != KEY
    ]
    if not non_key:
        return Image.new("RGB", (CELL_W, CELL_H), KEY)

    y0 = min(y for _, y in non_key)
    y1 = max(y for _, y in non_key) + 1
    painted = frame.crop((0, y0, CELL_W, y1))
    painted = painted.resize((CELL_W, target_height), Image.Resampling.NEAREST)

    result = Image.new("RGB", (CELL_W, CELL_H), KEY)
    # About 3% down, leaving a clean key-colored row above the nozzle-contact head.
    top = 30
    result.paste(painted, (0, top))
    return result


def build_sheet(source, heights):
    source = quantize(source)
    frames = []
    for i in range(FRAMES):
        left = (source.width * i) // FRAMES
        right = (source.width * (i + 1)) // FRAMES
        frames.append(make_frame(source.crop((left, 0, right, source.height)), heights[i]))

    sheet = Image.new("RGB", (CELL_W * FRAMES, CELL_H), KEY)
    for i, frame in enumerate(frames):
        sheet.paste(frame, (CELL_W * i, 0))
    return quantize(sheet)


def preview_layer(sheet):
    layer = Image.new("RGB", sheet.size)
    layer.putdata([PREVIEW_BG if px == KEY else px for px in sheet.get_flattened_data()])
    return layer


def main():
    source = Image.open(SOURCE)
    cruise = build_sheet(source, [770, 760, 940, 780, 770, 760, 760, 775])
    boost = build_sheet(source, [970, 960, 975, 980, 970, 960, 970, 975])
    cruise.save(ROOT / "flame_cruise.png", format="PNG", optimize=False)
    boost.save(ROOT / "flame_boost.png", format="PNG", optimize=False)

    preview = Image.new("RGB", (CELL_W * FRAMES, CELL_H * 2), PREVIEW_BG)
    preview.paste(preview_layer(cruise), (0, 0))
    preview.paste(preview_layer(boost), (0, CELL_H))
    preview.save(ROOT / "preview_j2.png", format="PNG", optimize=False)


if __name__ == "__main__":
    main()
