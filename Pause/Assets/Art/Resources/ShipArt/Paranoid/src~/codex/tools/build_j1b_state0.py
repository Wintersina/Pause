from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
MAGENTA = (255, 0, 255)
PREVIEW_BG = (11, 11, 26)
SIZE = 1024
SHEET = (SIZE * 3, SIZE * 2)


def ship_mask(image):
    a = np.asarray(image.convert("RGB"), dtype=np.uint8)
    # The generated field is vivid chroma magenta. Treat its near-magenta
    # variants as background before any painted region is sampled.
    return ~((a[:, :, 0] > 220) & (a[:, :, 2] > 220) & (a[:, :, 1] < 100))


def normalize_background(image):
    image = image.convert("RGB")
    a = np.asarray(image, dtype=np.uint8).copy()
    bg = ~ship_mask(image)
    a[bg] = MAGENTA
    return Image.fromarray(a, "RGB")


def bbox(mask):
    ys, xs = np.where(mask)
    return (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)


def fit_ship_to_key(candidate, key):
    candidate = normalize_background(candidate)
    cb = bbox(ship_mask(candidate))
    kb = bbox(ship_mask(key))
    crop = candidate.crop(cb)
    fitted = crop.resize((kb[2] - kb[0], kb[3] - kb[1]), Image.Resampling.NEAREST)
    canvas = Image.new("RGB", key.size, MAGENTA)
    canvas.paste(fitted, (kb[0], kb[1]))
    return canvas


def fit_bank(candidate, key):
    candidate = normalize_background(candidate)
    cb = bbox(ship_mask(candidate))
    kb = bbox(ship_mask(key))
    crop = candidate.crop(cb)
    # Keep the generated perspective painting intact and scale uniformly so
    # its height matches the approved idle silhouette height.
    scale = (kb[3] - kb[1]) / crop.height
    w = max(1, round(crop.width * scale))
    h = kb[3] - kb[1]
    fitted = crop.resize((w, h), Image.Resampling.NEAREST)
    canvas = Image.new("RGB", key.size, MAGENTA)
    x = round((SIZE - w) / 2)
    y = kb[1]
    canvas.paste(fitted, (x, y))
    return canvas


def polygon_mask(polygons):
    mask = Image.new("L", (SIZE, SIZE), 0)
    draw = ImageDraw.Draw(mask)
    for polygon in polygons:
        draw.polygon(polygon, fill=255)
    return mask


def paint_region(base, paint, mask):
    out = base.copy()
    out.paste(paint, (0, 0), mask)
    return out


def canopy_mask():
    return polygon_mask([
        [(508, 377), (622, 468), (622, 598), (511, 663),
         (399, 598), (399, 468)],
    ])


def lights_mask():
    return polygon_mask([
        [(430, 155), (590, 155), (620, 305), (414, 305)],
        [(430, 705), (590, 705), (604, 850), (418, 850)],
        [(47, 245), (250, 245), (250, 350), (47, 350)],
        [(790, 245), (981, 245), (981, 350), (790, 350)],
        [(48, 748), (242, 748), (242, 880), (48, 880)],
        [(790, 748), (981, 748), (981, 880), (790, 880)],
        [(396, 360), (628, 360), (628, 675), (396, 675)],
    ])


def add_right_glint(image):
    out = image.copy()
    draw = ImageDraw.Draw(out)
    faint = (164, 216, 220)
    fainter = (112, 181, 193)
    # A thin, deliberately stepped lower-left to upper-right streak on the
    # right half of the canopy; no smoothing or antialiasing.
    steps = [(532, 584), (545, 571), (558, 558), (571, 545),
             (584, 532), (597, 519)]
    for i, (x, y) in enumerate(steps):
        c = faint if i in (2, 3, 4) else fainter
        draw.rectangle((x, y, x + 2, y + 6), fill=c)
    draw.point((604, 512), fill=(225, 239, 227))
    return out


def add_left_glint_cleanup(image):
    out = image.copy()
    draw = ImageDraw.Draw(out)
    white = (245, 253, 245)
    bone = (255, 251, 224)
    for x, y in [(430 + i * 8, 594 - i * 8) for i in range(12)]:
        draw.rectangle((x, y, x + 3, y + 5), fill=white)
    draw.rectangle((520, 492, 528, 500), fill=bone)
    return out


def sanitize_ship_pink(image):
    # Generated candidates can leave isolated chroma-pink specks on the hull.
    # Replace those isolated pixels with ink, never with a pink ship pixel.
    a = np.asarray(image.convert("RGB"), dtype=np.uint8).copy()
    r, g, b = [a[:, :, i].astype(np.int16) for i in range(3)]
    pink = (r > g) & (b > g)
    bg = (r > 220) & (b > 220) & (g < 100)
    a[pink & ~bg] = (5, 6, 12)
    a[bg] = MAGENTA
    return Image.fromarray(a, "RGB")


def build_sheet():
    key = Image.open(ROOT / "key_state0.png").convert("RGB")
    lights_candidate = Image.open(ROOT / "candidate_lights_off.png")
    glint_candidate = Image.open(ROOT / "candidate_glint_left.png")
    bank_left_candidate = Image.open(ROOT / "candidate_bank_left.png")
    bank_right_candidate = Image.open(ROOT / "candidate_bank_right.png")

    lights_paint = fit_ship_to_key(lights_candidate, key)
    glint_paint = fit_ship_to_key(glint_candidate, key)

    lights_off = sanitize_ship_pink(paint_region(key, lights_paint, lights_mask()))
    glint_left = paint_region(key, glint_paint, canopy_mask())
    glint_left = sanitize_ship_pink(add_left_glint_cleanup(glint_left))
    glint_right = sanitize_ship_pink(add_right_glint(lights_off))
    bank_left = sanitize_ship_pink(fit_bank(bank_left_candidate, key))
    bank_right = sanitize_ship_pink(fit_bank(bank_right_candidate, key))

    sheet = Image.new("RGB", SHEET, MAGENTA)
    cells = [lights_off, glint_left, glint_right, bank_left, bank_right]
    for index, image in enumerate(cells):
        sheet.paste(image, ((index % 3) * SIZE, (index // 3) * SIZE))
    sheet.save(ROOT / "hull_redo_state0.png", format="PNG", optimize=False)

    # Review preview: approved key, final J1b sheet, and the bank modesty ref.
    preview = Image.new("RGB", (SIZE * 3, SIZE * 2), PREVIEW_BG)
    key_display = key.copy()
    sheet_display = sheet.copy()
    ref = Image.open(ROOT / "ref_neoncomet_banks.png").convert("RGB")
    ref_display = Image.new("RGB", (SIZE * 3, SIZE * 2), PREVIEW_BG)
    ref_fit = ref.resize((SIZE * 3, round(ref.height * (SIZE * 3 / ref.width))), Image.Resampling.NEAREST)
    ref_display.paste(ref_fit, (0, (SIZE * 2 - ref_fit.height) // 2))
    # Remove the review magenta field only; keep the source image unchanged.
    for display in (key_display, sheet_display, ref_display):
        arr = np.asarray(display, dtype=np.uint8).copy()
        field = np.all(arr == MAGENTA, axis=2)
        arr[field] = PREVIEW_BG
        display.paste(Image.fromarray(arr, "RGB"))
    preview.paste(key_display.resize((SIZE * 2 // 3, SIZE * 2 // 3), Image.Resampling.NEAREST), (0, SIZE // 6))
    preview.paste(sheet_display.resize((SIZE * 2 // 3, SIZE * 2 // 3), Image.Resampling.NEAREST), (SIZE, SIZE // 6))
    preview.paste(ref_display.resize((SIZE * 2 // 3, SIZE * 2 // 3), Image.Resampling.NEAREST), (SIZE * 2, SIZE // 6))
    preview.save(ROOT / "preview_j1b.png", format="PNG", optimize=False)


if __name__ == "__main__":
    build_sheet()
