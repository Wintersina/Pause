from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
TOOLS = Path(__file__).resolve().parent
GREEN = (0, 255, 0)
PREVIEW_BG = (11, 11, 26)


def load(path):
    image = Image.open(path).convert("RGB")
    if image.size != (1024, 1024):
        image = image.resize((1024, 1024), Image.Resampling.NEAREST)
    return image


def is_green(pixel):
    r, g, b = pixel
    return g >= 150 and g > r * 1.12 and g > b * 1.12


def key_generated_background(image):
    pixels = image.load()
    for y in range(1024):
        for x in range(1024):
            if is_green(pixels[x, y]):
                pixels[x, y] = GREEN
    return image


def changed(a, b, threshold=60):
    return sum(abs(x - y) for x, y in zip(a, b)) > threshold


def apply_reference_region(target, reference, boxes, base, threshold=60):
    target_pixels = target.load()
    reference_pixels = reference.load()
    base_pixels = base.load()
    for left, top, right, bottom in boxes:
        for y in range(max(0, top), min(1024, bottom)):
            for x in range(max(0, left), min(1024, right)):
                candidate = reference_pixels[x, y]
                if not is_green(candidate) and changed(candidate, base_pixels[x, y], threshold):
                    target_pixels[x, y] = candidate


def canopy_mask(key):
    mask = Image.new("1", (1024, 1024), 0)
    draw = ImageDraw.Draw(mask)
    draw.polygon(
        [(462, 245), (500, 220), (548, 223), (582, 273),
         (590, 405), (550, 468), (488, 447), (454, 382)],
        fill=1,
    )
    key_pixels = key.load()
    mask_pixels = mask.load()
    for y in range(1024):
        for x in range(1024):
            if mask_pixels[x, y]:
                r, g, b = key_pixels[x, y]
                if not (b > r * 1.05 and b > 45 and g > 35):
                    mask_pixels[x, y] = 0
    return mask


def apply_canopy_reference(target, reference, key, preserve_bright=False):
    target_pixels = target.load()
    reference_pixels = reference.load()
    key_pixels = key.load()
    mask_pixels = canopy_mask(key).load()
    for y in range(210, 475):
        for x in range(445, 600):
            if not mask_pixels[x, y] or is_green(reference_pixels[x, y]):
                continue
            if preserve_bright:
                r, g, b = key_pixels[x, y]
                if r > 195 and g > 195 and b > 180:
                    continue
            target_pixels[x, y] = reference_pixels[x, y]


def lights_off(key, lights_reference):
    image = key.copy()
    # These boxes isolate the four running-light panels, the reactor ring and canopy.
    apply_reference_region(
        image,
        lights_reference,
        [
            (145, 675, 325, 855),
            (695, 675, 885, 855),
            (35, 775, 225, 965),
            (815, 775, 990, 965),
            (375, 490, 650, 760),
            (445, 210, 600, 475),
        ],
        key,
    )
    return image


def glint_one(key, glint_reference):
    image = key.copy()
    apply_canopy_reference(image, glint_reference, key, preserve_bright=False)
    paint_streak(image, key, left=True, faint=False)
    return image


def glint_two(key, glint_reference):
    image = key.copy()
    # Start from the lights-off state outside this function. This pass adds a thinner,
    # fainter, right-half glint while leaving any wreck cracks untouched.
    target_pixels = image.load()
    source_pixels = glint_reference.load()
    key_pixels = key.load()
    mask_pixels = canopy_mask(key).load()
    for y in range(250, 445):
        for x in range(515, 585):
            if not mask_pixels[x, y] or is_green(source_pixels[x, y]):
                continue
            r, g, b = source_pixels[x, y]
            kr, kg, kb = key_pixels[x, y]
            if r + g + b > 650 and abs(r - kr) + abs(g - kg) + abs(b - kb) > 60:
                # A muted bone-white pixel; no interpolation or antialiasing.
                target_pixels[x, y] = (190, 218, 218)
    paint_streak(image, key, left=False, faint=True)
    return image


def paint_streak(image, key, left, faint):
    canopy = canopy_mask(key).load()
    overlay = image.copy()
    draw = ImageDraw.Draw(overlay)
    if left:
        points = [(477, 432), (492, 397), (509, 361), (526, 323), (543, 284), (557, 246)]
        outer = (150, 202, 211) if faint else (88, 205, 220)
        inner = (235, 246, 239) if faint else (255, 255, 242)
        width = 18 if faint else 72
    else:
        points = [(548, 431), (557, 398), (566, 365), (574, 332), (581, 301)]
        outer = (145, 193, 203)
        inner = (202, 227, 226)
        width = 10
    draw.line(points, fill=outer, width=width, joint="curve")
    draw.line(points, fill=inner, width=max(3, 8 if left and not faint else width // 3), joint="curve")
    tip_x, tip_y = points[-1]
    if left:
        draw.polygon([(tip_x, tip_y - 15), (tip_x + 7, tip_y),
                      (tip_x, tip_y + 15), (tip_x - 7, tip_y)], fill=inner)
    target_pixels = image.load()
    overlay_pixels = overlay.load()
    for y in range(210, 475):
        for x in range(445, 600):
            if canopy[x, y]:
                target_pixels[x, y] = overlay_pixels[x, y]


def build_sheet(index, key, lights_reference, glint_reference, left_bank, right_bank):
    off = lights_off(key, lights_reference)
    one = glint_one(key, glint_reference)
    two = glint_two(off, glint_reference)
    cells = [off, one, two, key_generated_background(left_bank),
             key_generated_background(right_bank), Image.new("RGB", (1024, 1024), GREEN)]
    sheet = Image.new("RGB", (3072, 2048), GREEN)
    for cell_index, cell in enumerate(cells):
        sheet.paste(cell, ((cell_index % 3) * 1024, (cell_index // 3) * 1024))
    sheet.save(ROOT / f"hull_redo_state{index}.png")
    return sheet


def make_preview(sheets):
    shown_width, shown_height = 1536, 1024
    gap = 24
    preview = Image.new("RGB", (shown_width * 3 + gap * 2, shown_height), PREVIEW_BG)
    for index, sheet in enumerate(sheets):
        scaled = sheet.resize((shown_width, shown_height), Image.Resampling.NEAREST)
        pixels = scaled.load()
        for y in range(shown_height):
            for x in range(shown_width):
                if pixels[x, y] == GREEN:
                    pixels[x, y] = PREVIEW_BG
        preview.paste(scaled, (index * (shown_width + gap), 0))
    preview.save(ROOT / "preview_j1b.png")


def main():
    keys = [load(ROOT / f"key_state{index}.png") for index in range(3)]
    lights_reference = key_generated_background(load(TOOLS / "_j1b_lights_off.png"))
    glint_reference = key_generated_background(load(TOOLS / "_j1b_glint.png"))
    bank_sets = [
        ("_j1b_bank0_left.png", "_j1b_bank0_right.png"),
        ("_j1b_bank1_left.png", "_j1b_bank1_right.png"),
        ("_j1b_bank2_left.png", "_j1b_bank2_right.png"),
    ]
    sheets = []
    for index, key in enumerate(keys):
        left = key_generated_background(load(TOOLS / bank_sets[index][0]))
        right = key_generated_background(load(TOOLS / bank_sets[index][1]))
        sheets.append(build_sheet(index, key, lights_reference, glint_reference, left, right))
    make_preview(sheets)
    print("built hull_redo_state0.png, hull_redo_state1.png, hull_redo_state2.png, preview_j1b.png")


if __name__ == "__main__":
    main()
