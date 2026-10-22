from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
SIZE = (1024, 1024)
KEY_PATH = ROOT / "key_state0.png"
LEFT_SOURCE = Path(
    "/Users/sina/.codex/generated_images/01a1649a-94cc-7893-a482-7e3644ffd407/"
    "exec-6ab9a24d-9f14-4f92-9a1a-cbfc3e2c5716.png"
)
RIGHT_SOURCE = Path(
    "/Users/sina/.codex/generated_images/01a1649a-94cc-7893-a482-7e3644ffd407/"
    "exec-08bdc5bf-e34a-4ae6-99ed-03640a09ff90.png"
)


def key_green(image: Image.Image) -> Image.Image:
    image = image.convert("RGB")
    pixels = image.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b = pixels[x, y]
            if g > 140 and r < 100 and b < 100 and g > r * 1.8 and g > b * 1.8:
                pixels[x, y] = GREEN
    return image


def bbox(image: Image.Image):
    points = [
        (x, y)
        for y in range(image.height)
        for x in range(image.width)
        if image.getpixel((x, y)) != GREEN
    ]
    xs = [point[0] for point in points]
    ys = [point[1] for point in points]
    return min(xs), min(ys), max(xs), max(ys)


def fit_bank(path: Path, key: Image.Image) -> Image.Image:
    source = key_green(Image.open(path)).resize(SIZE, Image.Resampling.NEAREST)
    source_box = bbox(source)
    key_box = bbox(key)
    source_cx = (source_box[0] + source_box[2]) / 2
    source_cy = (source_box[1] + source_box[3]) / 2
    key_cx = (key_box[0] + key_box[2]) / 2
    key_cy = (key_box[1] + key_box[3]) / 2
    dx = round(key_cx - source_cx)
    dy = round(key_cy - source_cy)
    aligned = Image.new("RGB", SIZE, GREEN)
    aligned.paste(source, (dx, dy))
    return aligned


def polygon_mask(points):
    mask = Image.new("1", SIZE, 0)
    ImageDraw.Draw(mask).polygon(points, fill=1)
    return mask


def recolor_mask(image, mask, transform):
    pixels = image.load()
    for y in range(SIZE[1]):
        for x in range(SIZE[0]):
            if mask.getpixel((x, y)) and pixels[x, y] != GREEN:
                pixels[x, y] = transform(pixels[x, y])


def dim_rgb(rgb, factor):
    return tuple(max(0, min(255, round(value * factor))) for value in rgb)


def dark_running_lights(image):
    # Tight masks cover the two red/white diamond lamps and their short red
    # streaks without touching the adjacent gunmetal flipper panels.
    lamp_polys = [
        [(176, 164), (199, 144), (225, 177), (203, 226), (176, 201)],
        [(799, 164), (824, 144), (849, 177), (826, 226), (799, 201)],
        [(480, 914), (545, 914), (547, 972), (479, 972)],
    ]
    for points in lamp_polys:
        mask = polygon_mask(points)
        recolor_mask(image, mask, lambda rgb: (43, 13, 30) if rgb[0] >= rgb[2] else (18, 23, 45))


def dim_centre_lamp(image):
    mask = polygon_mask([(478, 548), (548, 548), (557, 581), (548, 611), (478, 611), (468, 581)])
    recolor_mask(image, mask, lambda rgb: dim_rgb(rgb, 0.48))


def dim_canopy(image):
    mask = polygon_mask([(512, 184), (560, 208), (560, 292), (512, 320), (464, 292), (464, 208)])
    recolor_mask(image, mask, lambda rgb: dim_rgb(rgb, 0.86))


def cyan_or_light(rgb):
    r, g, b = rgb
    return b > 80 and g > 80 and b > r * 1.12 or (r > 175 and g > 175 and b > 175)


def glint_left(image):
    # Lift the whole canopy gem by one hard pixel-art colour step so the
    # glint reads at sprite-sheet scale, then add the explicit stepped streak.
    gem = polygon_mask([(512, 184), (560, 208), (560, 292), (512, 320), (464, 292), (464, 208)])
    pixels = image.load()
    for y in range(SIZE[1]):
        for x in range(464, 561):
            if gem.getpixel((x, y)) and pixels[x, y] != GREEN:
                r, g, b = pixels[x, y]
                if r > 220 and g > 220 and b > 220:
                    pixels[x, y] = (185, 255, 255)
                elif b > r and g > r:
                    pixels[x, y] = (min(255, r + 35), max(0, g - 35), b)
                else:
                    pixels[x, y] = (min(255, r + 25), min(255, g + 25), min(255, b + 25))
    draw = ImageDraw.Draw(image)
    steps = [(476 + i * 5, 292 - i * 8) for i in range(10)]
    for x, y in steps:
        draw.rectangle((x, y, x + 3, y + 3), fill=(245, 253, 244))
    draw.polygon([(520, 211), (524, 216), (520, 221), (515, 216)], fill=(255, 251, 224))
    draw.point((520, 208), fill=(255, 255, 255))


def glint_right_faint(image):
    gem = polygon_mask([(512, 184), (560, 208), (560, 292), (512, 320), (464, 292), (464, 208)])
    pixels = image.load()
    for y in range(SIZE[1]):
        for x in range(512, 561):
            if gem.getpixel((x, y)) and pixels[x, y] != GREEN and cyan_or_light(pixels[x, y]):
                r, g, b = pixels[x, y]
                pixels[x, y] = (min(255, r + 5), min(255, g + 7), min(255, b + 8))
    draw = ImageDraw.Draw(image)
    for i in range(8):
        x, y = 527 + i * 4, 220 + i * 7
        draw.rectangle((x, y, x + 1, y + 2), fill=(160, 238, 238))
    draw.point((558, 275), fill=(212, 247, 237))


def save_sheet(key, left, right):
    cell0 = key.copy()
    dark_running_lights(cell0)
    dim_centre_lamp(cell0)
    dim_canopy(cell0)

    cell1 = key.copy()
    glint_left(cell1)

    cell2 = key.copy()
    dark_running_lights(cell2)
    glint_right_faint(cell2)

    empty = Image.new("RGB", SIZE, GREEN)
    sheet = Image.new("RGB", (3072, 2048), GREEN)
    cells = (cell0, cell1, cell2, left, right, empty)
    for index, cell in enumerate(cells):
        sheet.paste(cell, ((index % 3) * 1024, (index // 3) * 1024))
    sheet.save(ROOT / "hull_redo_state0.png", format="PNG", optimize=False)

    preview = Image.new("RGB", (3072, 2048), (11, 11, 26))
    for index, cell in enumerate(cells):
        mask = Image.new("1", SIZE, 0)
        mask_data = mask.load()
        cell_data = cell.load()
        for y in range(1024):
            for x in range(1024):
                mask_data[x, y] = cell_data[x, y] != GREEN
        preview.paste(cell, ((index % 3) * 1024, (index // 3) * 1024), mask)
    preview.save(ROOT / "preview_j1b.png", format="PNG", optimize=False)


if __name__ == "__main__":
    key = Image.open(KEY_PATH).convert("RGB")
    left = fit_bank(LEFT_SOURCE, key)
    right = fit_bank(RIGHT_SOURCE, key)
    save_sheet(key, left, right)
