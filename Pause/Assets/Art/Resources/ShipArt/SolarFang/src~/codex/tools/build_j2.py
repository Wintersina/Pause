from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
SOURCE = Path(__file__).resolve().parent / "_j2_generated_source.png"
CELL_W = 256
CELL_H = 1024
FRAME_COUNT = 7
GREEN = (0, 255, 0)
BURNT = (0x9A, 0x3E, 0x12)
ORANGE = (0xF0, 0x7A, 0x1E)
AMBER = (0xFF, 0xB4, 0x3C)
BONE = (0xF4, 0xEA, 0xD4)
SPARK = (0xF2, 0x86, 0x2B)
PALETTE = (BURNT, ORANGE, AMBER, BONE, SPARK)


def is_key(pixel):
    r, g, b = pixel
    return g > 150 and g > r * 1.25 + 8 and g > b * 1.25 + 8


def quantize(source):
    pixels = []
    for pixel in source.convert("RGB").getdata():
        if is_key(pixel):
            pixels.append(GREEN)
            continue
        pixels.append(min(PALETTE, key=lambda colour: sum((a - b) ** 2 for a, b in zip(pixel, colour))))
    result = Image.new("RGB", source.size, GREEN)
    result.putdata(pixels)
    return result


def non_key_bbox(image):
    points = []
    for y in range(image.height):
        for x in range(image.width):
            if image.getpixel((x, y)) != GREEN:
                points.append((x, y))
    if not points:
        raise ValueError("source panel has no flame pixels")
    xs = [point[0] for point in points]
    ys = [point[1] for point in points]
    return min(xs), min(ys), max(xs) + 1, max(ys) + 1


def source_panels(image):
    panels = []
    for index in range(FRAME_COUNT):
        left = round(index * image.width / FRAME_COUNT)
        right = round((index + 1) * image.width / FRAME_COUNT)
        panel = image.crop((left, 0, right, image.height))
        box = non_key_bbox(panel)
        pad_x = max(2, (box[2] - box[0]) // 30)
        pad_y = max(2, (box[3] - box[1]) // 40)
        box = (
            max(0, box[0] - pad_x),
            max(0, box[1] - pad_y),
            min(panel.width, box[2] + pad_x),
            min(panel.height, box[3] + pad_y),
        )
        panels.append(panel.crop(box))
    return panels


def paste_pose(target, panel, width, height, top, x_shift=0):
    pose = panel.resize((width, height), Image.Resampling.NEAREST)
    x = (CELL_W - width) // 2 + x_shift
    for y in range(height):
        for px in range(width):
            colour = pose.getpixel((px, y))
            if colour != GREEN:
                target.putpixel((x + px, top + y), colour)


def widen_core(cell, frame_index, smear=False):
    draw = ImageDraw.Draw(cell)
    for y in range(24, CELL_H):
        run = [x for x in range(CELL_W) if cell.getpixel((x, y)) != GREEN]
        if not run:
            continue
        left, right = min(run), max(run)
        span = max(1, right - left)
        centre = (left + right) / 2.0
        half = span * (0.24 if smear else 0.16)
        for x in range(CELL_W):
            if abs(x - centre) <= half and cell.getpixel((x, y)) in (BURNT, ORANGE, AMBER, SPARK):
                draw.point((x, y), fill=BONE)


def add_sparks(cell, frame_index, boost):
    draw = ImageDraw.Draw(cell)
    count = 7 if boost else 3
    if frame_index == 2:
        count = 4 if boost else 2
    for spark_index in range(count):
        seed = frame_index * 19 + spark_index * 31 + (7 if boost else 0)
        side = -1 if seed % 2 else 1
        x = CELL_W // 2 + side * (48 + (seed * 13) % 65)
        y = (155 + seed * (29 if boost else 23)) % (910 if boost else 750) + (50 if boost else 80)
        size = 2 + seed % 4
        colour = SPARK if seed % 3 else AMBER
        if frame_index == 2:
            size = max(1, size - 1)
        draw.rectangle((x, y, x + size, y + max(1, size // 2)), fill=colour)


def make_sheet(panels, boost):
    sheet = Image.new("RGB", (CELL_W * FRAME_COUNT, CELL_H), GREEN)
    cruise_heights = (770, 810, 900, 790, 800, 830, 785)
    cruise_widths = (188, 184, 98, 186, 190, 186, 184)
    boost_heights = (930, 950, 965, 945, 938, 955, 944)
    boost_widths = (194, 190, 92, 192, 198, 194, 190)
    heights = boost_heights if boost else cruise_heights
    widths = boost_widths if boost else cruise_widths
    shifts = (-3, 2, -1, 3, -2, 2, 0)
    for index, panel in enumerate(panels):
        cell = Image.new("RGB", (CELL_W, CELL_H), GREEN)
        paste_pose(cell, panel, widths[index], heights[index], 30, shifts[index])
        if boost:
            widen_core(cell, index, smear=index == 2)
        add_sparks(cell, index, boost)
        sheet.paste(cell, (index * CELL_W, 0))
    return sheet


def main():
    source = quantize(Image.open(SOURCE))
    panels = source_panels(source)
    cruise = make_sheet(panels, boost=False)
    boost = make_sheet(panels, boost=True)
    cruise.save(ROOT / "flame_cruise.png", format="PNG", optimize=False)
    boost.save(ROOT / "flame_boost.png", format="PNG", optimize=False)
    print("wrote flame_cruise.png and flame_boost.png")


if __name__ == "__main__":
    main()
