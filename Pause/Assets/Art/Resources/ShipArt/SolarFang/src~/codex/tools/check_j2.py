from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
FILES = (ROOT / "flame_cruise.png", ROOT / "flame_boost.png")
GREEN = (0, 255, 0)
BG = (0x0B, 0x0B, 0x1A)
PALETTE = {
    (0x9A, 0x3E, 0x12),
    (0xF0, 0x7A, 0x1E),
    (0xFF, 0xB4, 0x3C),
    (0xF4, 0xEA, 0xD4),
    (0xF2, 0x86, 0x2B),
}
ALLOWED = PALETTE | {GREEN}
CELL_W = 256
CELL_H = 1024
FRAME_COUNT = 7


def bbox(cell):
    points = []
    for y in range(cell.height):
        for x in range(cell.width):
            if cell.getpixel((x, y)) != GREEN:
                points.append((x, y))
    if not points:
        return None
    xs = [point[0] for point in points]
    ys = [point[1] for point in points]
    return min(xs), min(ys), max(xs), max(ys)


def make_preview(images):
    preview_width = CELL_W * FRAME_COUNT
    preview_height = 2 * CELL_H + 32
    preview = Image.new("RGB", (preview_width, preview_height), BG)
    for index, image in enumerate(images):
        display = Image.new("RGB", image.size, BG)
        for y in range(image.height):
            for x in range(image.width):
                colour = image.getpixel((x, y))
                if colour != GREEN:
                    display.putpixel((x, y), colour)
        preview.paste(display, (0, index * (CELL_H + 32)))
    draw = ImageDraw.Draw(preview)
    draw.rectangle((0, CELL_H, preview_width - 1, CELL_H + 31), fill=BG)
    preview.save(ROOT / "preview_j2.png", format="PNG", optimize=False)


def main():
    images = []
    failures = []
    print("file           size         unique  bad  frame  bbox (x0,y0,x1,y1)")
    for path in FILES:
        image = Image.open(path).convert("RGB")
        images.append(image)
        colours = set(image.getdata())
        bad = colours - ALLOWED
        frame_results = []
        if image.size != (CELL_W * FRAME_COUNT, CELL_H):
            failures.append(f"{path.name}: expected {CELL_W * FRAME_COUNT}x{CELL_H}, got {image.size}")
        if bad:
            failures.append(f"{path.name}: disallowed colours {sorted(bad)[:5]}")
        for index in range(FRAME_COUNT):
            cell = image.crop((index * CELL_W, 0, (index + 1) * CELL_W, CELL_H))
            frame_results.append(bbox(cell))
            if cell.size != (CELL_W, CELL_H):
                failures.append(f"{path.name}: cell {index} has size {cell.size}")
        all_equal = image.width == CELL_W * FRAME_COUNT and image.width // FRAME_COUNT == CELL_W
        if not all_equal:
            failures.append(f"{path.name}: cells are not equal {CELL_W}px widths")
        first_box = frame_results[0]
        print(f"{path.name:<14} {image.width}x{image.height:<7} {len(colours):>7} {len(bad):>4}    {len(frame_results):>1}    {first_box}")
        for index, frame_box in enumerate(frame_results):
            print(f"  frame {index}: {frame_box}")
    make_preview(images)
    print("preview_j2.png: written on #0b0b1a")
    if failures:
        raise SystemExit("\nFAIL\n" + "\n".join(failures))
    print("PASS")


if __name__ == "__main__":
    main()
