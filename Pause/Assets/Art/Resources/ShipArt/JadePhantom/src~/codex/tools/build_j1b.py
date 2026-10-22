"""Build the J1b pose sheets from the approved keys and painted bank sources."""

from collections import deque
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
GREEN = (0, 255, 0)
NAVY = (5, 18, 28)
DARK_GREEN = (7, 49, 38)

# These are generated, inspected perspective redraws; the key images are never
# geometrically transformed.  They are kept outside the project as generation
# working files, and only their selected painted pixels are read here.
BANK_LEFT = Path("/Users/sina/.codex/generated_images/01a16004-9e7c-7f52-ae70-d40a31ee129d/exec-2e3d1306-17b0-4a9f-8887-a268153a7118.png")
BANK_RIGHT = Path("/Users/sina/.codex/generated_images/01a16004-9e7c-7f52-ae70-d40a31ee129d/exec-c1e70d0d-83db-4a1a-a791-d85a76039954.png")


def rgb(path):
    image = Image.open(path).convert("RGB")
    if image.size != (1024, 1024):
        image = image.resize((1024, 1024), Image.Resampling.NEAREST)
    return image


def is_bg(pixel):
    r, g, b = pixel
    return g > 190 and r < 105 and b < 105 and g - r > 100 and g - b > 100


def flatten_background(image):
    """Remove only the connected bright-green background, preserving the ship."""
    pixels = image.load()
    seen = bytearray(1024 * 1024)
    queue = deque()
    for x in range(1024):
        queue.append((x, 0))
        queue.append((x, 1023))
    for y in range(1024):
        queue.append((0, y))
        queue.append((1023, y))
    while queue:
        x, y = queue.popleft()
        index = y * 1024 + x
        if seen[index] or not is_bg(pixels[x, y]):
            continue
        seen[index] = 1
        pixels[x, y] = GREEN
        if x:
            queue.append((x - 1, y))
        if x < 1023:
            queue.append((x + 1, y))
        if y:
            queue.append((x, y - 1))
        if y < 1023:
            queue.append((x, y + 1))
    return image


def non_green_bbox(image):
    points = [(x, y) for y in range(1024) for x in range(1024) if image.getpixel((x, y)) != GREEN]
    return (
        min(x for x, _ in points), min(y for _, y in points),
        max(x for x, _ in points) + 1, max(y for _, y in points) + 1,
    )


def align_to_key(image, key):
    """Integer-anchor a generated perspective redraw to the approved key."""
    source = non_green_bbox(image)
    target = non_green_bbox(key)
    sx = (source[0] + source[2]) // 2
    sy = (source[1] + source[3]) // 2
    tx = (target[0] + target[2]) // 2
    ty = (target[1] + target[3]) // 2
    dx, dy = tx - sx, ty - sy
    canvas = Image.new("RGB", (1024, 1024), GREEN)
    canvas.paste(image, (dx, dy))
    return canvas


def dim_pixel(pixel, factor=0.42):
    return tuple(max(0, int(channel * factor)) for channel in pixel)


def lights_off(image):
    image = image.copy()
    pixels = image.load()
    # Tip diamonds: keep their dark panel outline, replace only emissive pixels.
    for box in ((78, 315, 122, 397), (900, 315, 944, 397)):
        for y in range(box[1], box[3]):
            for x in range(box[0], box[2]):
                r, g, b = pixels[x, y]
                if pixels[x, y] != GREEN and (r > 125 or (r + g + b) > 500):
                    pixels[x, y] = NAVY if (r + g + b) > 520 else DARK_GREEN
    # Spine bar, gold star, and canopy glow.  These are deliberately hard pixel ramps.
    for y in range(420, 606):
        for x in range(485, 538):
            r, g, b = pixels[x, y]
            if pixels[x, y] != GREEN and (r > 140 or g > 150):
                pixels[x, y] = (20, 60, 48) if y % 3 else NAVY
    for y in range(650, 725):
        for x in range(484, 541):
            r, g, b = pixels[x, y]
            if pixels[x, y] != GREEN and r > 120 and g > 75 and b < 100:
                pixels[x, y] = (72, 62, 18)
    for y in range(175, 325):
        for x in range(462, 565):
            r, g, b = pixels[x, y]
            if b > 100 and g > 70 and b > r * 1.25:
                pixels[x, y] = dim_pixel(pixels[x, y], 0.72)
    return image


def add_glint(image, right=False, faint=False):
    image = image.copy()
    draw = ImageDraw.Draw(image)
    pixels = image.load()
    # A broad, stepped canopy sheen makes the glint read at phone scale while
    # remaining strictly inside the teal glass; the hard white core is added below.
    if not right:
        for y in range(140, 351):
            for x in range(430, 591):
                r, g, b = pixels[x, y]
                if pixels[x, y] != GREEN:
                    lift = 24 if x < 522 else 22
                    pixels[x, y] = (min(255, r + lift), min(255, g + lift), min(255, b + lift))
    else:
        for y in range(170, 326):
            for x in range(520, 565):
                r, g, b = pixels[x, y]
                if b > 100 and g > 70 and b > r * 1.2:
                    pixels[x, y] = (min(255, r + 10), min(255, g + 10), min(255, b + 10))
    if right:
        points = [(524, 296), (532, 286), (540, 276), (548, 266), (556, 256)]
        width = 2
        white = (177, 229, 229) if faint else (230, 250, 240)
        tip = (558, 253)
    else:
        points = [(478, 304), (486, 294), (494, 284), (502, 274), (510, 264), (518, 254)]
        width = 3
        white = (245, 253, 245)
        tip = (520, 251)
    draw.line(points, fill=white, width=width)
    if not faint:
        draw.point(tip, fill=(255, 255, 224))
        draw.point((tip[0] + 2, tip[1]), fill=(255, 255, 224))
        draw.point((tip[0], tip[1] + 2), fill=(255, 255, 224))
    return image


def transfer_damage(base, key0, damaged):
    """Paint the approved damage treatment onto the generated perspective redraw."""
    base = base.copy()
    bp, p0, pd = base.load(), key0.load(), damaged.load()
    for y in range(1024):
        for x in range(1024):
            if p0[x, y] == GREEN or pd[x, y] == GREEN:
                continue
            if sum(abs(pd[x, y][i] - p0[x, y][i]) for i in range(3)) > 60:
                # Keep the selected perspective's silhouette; this is a localized
                # material/damage paint transfer, never a geometric copy.
                if bp[x, y] != GREEN:
                    bp[x, y] = pd[x, y]
    return base


def make_sheet(state, key, bank_left, bank_right):
    cells = [
        lights_off(key),
        add_glint(key),
        add_glint(lights_off(key), right=True, faint=True),
        bank_left,
        bank_right,
        Image.new("RGB", (1024, 1024), GREEN),
    ]
    sheet = Image.new("RGB", (3072, 2048), GREEN)
    for index, cell in enumerate(cells):
        sheet.paste(cell, ((index % 3) * 1024, (index // 3) * 1024))
    path = ROOT / f"hull_redo_state{state}.png"
    sheet.save(path)
    return path


def main():
    keys = [rgb(ROOT / f"key_state{state}.png") for state in range(3)]
    left_source = align_to_key(flatten_background(rgb(BANK_LEFT)), keys[0])
    right_source = align_to_key(flatten_background(rgb(BANK_RIGHT)), keys[0])
    for state, key in enumerate(keys):
        left = transfer_damage(left_source, keys[0], key) if state else left_source.copy()
        right = transfer_damage(right_source, keys[0], key) if state else right_source.copy()
        make_sheet(state, key, left, right)

    preview = Image.new("RGB", (2304, 512), (11, 11, 26))
    for index in range(3):
        sheet = rgb(ROOT / f"hull_redo_state{index}.png")
        thumb = sheet.resize((768, 512), Image.Resampling.NEAREST)
        thumb_pixels = thumb.load()
        for y in range(512):
            for x in range(768):
                if thumb_pixels[x, y] == GREEN:
                    thumb_pixels[x, y] = (11, 11, 26)
        preview.paste(thumb, (index * 768, 0))
    preview.save(ROOT / "preview_j1b.png")


if __name__ == "__main__":
    main()
