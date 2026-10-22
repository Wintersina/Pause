from pathlib import Path

import colorsys

from PIL import Image, ImageChops, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
MAGENTA = (255, 0, 255)
PREVIEW_BG = (11, 11, 26)
SIZE = 1024
SHEET_SIZE = (SIZE * 3, SIZE * 2)

KEYS = (
    ROOT / "key_state0.png",
    ROOT / "key_state1.png",
    ROOT / "key_state2.png",
)

# These are generated-paint candidates selected after visual inspection. They are only
# used as painted source regions; final assembly is nearest-neighbour raster compositing.
GEN_BANK_LEFT = Path(
    "/Users/sina/.codex/generated_images/01a160c6-7695-7673-af32-eb26d705c9fc/"
    "exec-7b3f3658-07df-48a4-b424-234832833809.png"
)
GEN_LIGHTS_OFF = Path(
    "/Users/sina/.codex/generated_images/01a160c6-7695-7673-af32-eb26d705c9fc/"
    "exec-b09d4036-8d0c-43b2-b388-1b2582c2358a.png"
)
GEN_GLINT_LEFT = Path(
    "/Users/sina/.codex/generated_images/01a160c6-7695-7673-af32-eb26d705c9fc/"
    "exec-20a171cf-5e2f-4609-b13f-213d00b838bb.png"
)


def chroma_normalize(image):
    image = image.convert("RGB")
    px = image.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b = px[x, y]
            # Generated chroma fields are near-magenta. Preserve the ship's red and
            # warm lights, but lock the empty field to the exact requested key.
            h, s, _ = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
            if s > 0.20 and 280.0 / 360.0 <= h <= 345.0 / 360.0:
                px[x, y] = MAGENTA
    return image


def load_key(path):
    image = Image.open(path).convert("RGB")
    if image.size != (SIZE, SIZE):
        raise ValueError(f"{path} must be 1024x1024")
    return image


def load_generated(path):
    image = Image.open(path).convert("RGB")
    image = image.resize((SIZE, SIZE), Image.Resampling.NEAREST)
    return chroma_normalize(image)


def cell_copy(image):
    return image.copy()


def mask_from_polygons(polygons):
    mask = Image.new("L", (SIZE, SIZE), 0)
    draw = ImageDraw.Draw(mask)
    for polygon in polygons:
        draw.polygon(polygon, fill=255)
    return mask


def masked_paint(base, paint, mask):
    result = base.copy()
    result.paste(paint, (0, 0), mask)
    return result


def apply_candidate_regions(base, candidate, regions):
    mask = mask_from_polygons(regions)
    # Never let a generated chroma field replace the exact key field.
    candidate = candidate.copy()
    candidate.paste(MAGENTA, (0, 0), ImageChops.invert(candidate.getchannel("R")))
    return masked_paint(base, candidate, mask)


def hard_mask_difference(a, b, threshold=60):
    mask = Image.new("L", (SIZE, SIZE), 0)
    ap = a.load()
    bp = b.load()
    mp = mask.load()
    for y in range(SIZE):
        for x in range(SIZE):
            if sum(abs(ap[x, y][i] - bp[x, y][i]) for i in range(3)) >= threshold:
                mp[x, y] = 255
    return mask


def damage_overlay(base, key, clean):
    """Carry the approved damage pixels onto a painted bank pose without mirroring them."""
    result = base.copy()
    damage = hard_mask_difference(clean, key)
    # Damage masks can include the chroma field around a blown-out edge. Never
    # paste that field into a painted bank pose.
    non_magenta = Image.new("L", (SIZE, SIZE), 0)
    np = non_magenta.load()
    kp = key.load()
    mp = damage.load()
    for y in range(SIZE):
        for x in range(SIZE):
            if mp[x, y] and kp[x, y] != MAGENTA:
                np[x, y] = 255
    damage = non_magenta
    result.paste(key, (0, 0), damage)
    return result


def paint_lights_off(key, candidate):
    # The generated off-candidate is used only over the explicitly emissive panels.
    regions = [
        [(440, 158), (585, 158), (610, 300), (420, 300)],       # top diamond
        [(430, 700), (590, 700), (600, 835), (420, 835)],       # bottom diamond
        [(48, 248), (250, 248), (250, 342), (48, 342)],         # left red cap
        [(790, 248), (982, 248), (982, 342), (790, 342)],       # right red cap
        [(50, 755), (240, 842), (240, 875), (50, 875)],         # left nozzle
        [(790, 755), (980, 842), (980, 875), (790, 875)],       # right nozzle
        [(420, 370), (610, 370), (620, 660), (410, 660)],       # canopy glow
    ]
    result = key.copy()
    mask = mask_from_polygons(regions)
    result.paste(candidate, (0, 0), mask)
    return result


def paint_glint_left(key, candidate):
    # Candidate paint is clipped to the teal canopy only.
    canopy = mask_from_polygons([
        [(507, 378), (620, 470), (620, 596), (510, 660),
         (402, 596), (402, 470)],
    ])
    result = key.copy()
    result.paste(candidate, (0, 0), canopy)
    # Reinforce the generated glint with a few crisp, bone-white step pixels so
    # the sheet remains legible at native scale.
    draw = ImageDraw.Draw(result)
    for x, y in [(435 + i * 8, 586 - i * 8) for i in range(11)]:
        draw.rectangle((x, y, x + 3, y + 5), fill=(245, 253, 245))
    draw.line((425, 625, 520, 530), fill=(245, 253, 245), width=4)
    draw.line((405, 605, 505, 505), fill=(224, 242, 236), width=4)
    for x, y in ((435, 600), (455, 580), (475, 560)):
        draw.rectangle((x, y, x + 15, y + 15), fill=(255, 251, 224))
    draw.rectangle((523, 494, 531, 502), fill=(255, 251, 224))
    return result


def paint_glint_right_dimmed(lights_off):
    result = lights_off.copy()
    draw = ImageDraw.Draw(result)
    # Thin, fainter, hard staircase on the right half of the canopy.
    points = [(530, 574), (546, 558), (562, 542), (578, 526), (594, 510)]
    for x, y in points:
        draw.rectangle((x, y, x + 2, y + 5), fill=(180, 224, 225))
    draw.point((600, 503), fill=(245, 248, 230))
    draw.point((601, 503), fill=(245, 248, 230))
    return result


def add_bank_right_asymmetry(image):
    """Add hard-pixel right-bank side-wall paint so it is not a horizontal copy."""
    result = image.copy()
    original = image.copy()
    draw = ImageDraw.Draw(result)
    # Left pod is nearer: a stepped gunmetal outer wall and a slightly extended cap.
    draw.polygon([(63, 188), (102, 174), (116, 194), (108, 820), (72, 844)], fill=(11, 15, 21))
    draw.line([(82, 202), (77, 805)], fill=(122, 144, 148), width=3)
    draw.rectangle((85, 182, 190, 194), fill=(65, 79, 88))
    draw.rectangle((88, 184, 172, 188), fill=(166, 193, 188))
    # Right pod is tucked behind: dark overlap at its inner edge and a shortened cap.
    draw.polygon([(900, 214), (952, 204), (963, 788), (934, 816), (900, 790)], fill=(22, 34, 30))
    draw.line([(905, 225), (907, 774)], fill=(54, 84, 68), width=3)
    draw.rectangle((840, 188, 938, 201), fill=(30, 44, 43))
    # Visible left rim wall on the disc, with a few pixel stair highlights.
    draw.line([(294, 284), (252, 430), (265, 630), (320, 750)], fill=(70, 103, 93), width=5)
    draw.line([(298, 286), (258, 432), (270, 620)], fill=(131, 191, 112), width=2)
    # Keep all redraw pixels attached to the painted silhouette; no detached bars.
    rp = result.load()
    op = original.load()
    for y in range(SIZE):
        for x in range(SIZE):
            if op[x, y] == MAGENTA:
                rp[x, y] = MAGENTA
    return result


def bank_left_for_state(state, bank_candidate, keys):
    bank = bank_candidate.copy()
    if state:
        bank = damage_overlay(bank, keys[state], keys[0])
    return bank


def bank_right_for_state(state, bank_left, keys):
    # Mirror only the broad banked silhouette as a starting painted study, then
    # redraw asymmetric side walls and restore damage on its original physical panels.
    bank = bank_left.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    bank = add_bank_right_asymmetry(bank)
    if state:
        bank = damage_overlay(bank, keys[state], keys[0])
    return bank


def build_sheet(state, keys, bank_candidate, lights_candidate, glint_candidate):
    key = keys[state]
    lights_off = paint_lights_off(key, lights_candidate)
    glint_left = paint_glint_left(key, glint_candidate)
    glint_right = paint_glint_right_dimmed(lights_off)
    bank_left = bank_left_for_state(state, bank_candidate, keys)
    bank_right = bank_right_for_state(state, bank_left, keys)

    sheet = Image.new("RGB", SHEET_SIZE, MAGENTA)
    cells = (lights_off, glint_left, glint_right, bank_left, bank_right, None)
    for index, cell in enumerate(cells):
        if cell is not None:
            sheet.paste(cell, ((index % 3) * SIZE, (index // 3) * SIZE))
    return sheet


def build_preview(sheets):
    preview = Image.new("RGB", (SIZE * 9, SIZE * 2), PREVIEW_BG)
    for index, sheet in enumerate(sheets):
        display = sheet.copy()
        dp = display.load()
        for y in range(display.height):
            for x in range(display.width):
                if dp[x, y] == MAGENTA:
                    dp[x, y] = PREVIEW_BG
        preview.paste(display, (index * SIZE * 3, 0))
    return preview


def main():
    keys = tuple(load_key(path) for path in KEYS)
    bank_candidate = load_generated(GEN_BANK_LEFT)
    lights_candidate = load_generated(GEN_LIGHTS_OFF)
    glint_candidate = load_generated(GEN_GLINT_LEFT)

    sheets = []
    for state in range(3):
        sheet = build_sheet(state, keys, bank_candidate, lights_candidate, glint_candidate)
        path = ROOT / f"hull_redo_state{state}.png"
        sheet.save(path, format="PNG", optimize=False)
        sheets.append(sheet)
    build_preview(sheets).save(ROOT / "preview_j1b.png", format="PNG", optimize=False)


if __name__ == "__main__":
    main()
